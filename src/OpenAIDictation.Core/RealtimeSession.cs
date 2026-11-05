using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenAIDictation.Core;

/// <summary>One manually ended speech turn, with bounded buffering and a single ordered socket writer.</summary>
public sealed class RealtimeSession : IAsyncDisposable
{
    internal const int MaxPendingBytes = 4 * 1024 * 1024;
    internal const int MaxEventBytes = 256 * 1024;
    private readonly string _key;
    private readonly DictationOptions _options;
    private readonly IRealtimeTransport _transport;
    private readonly Channel<byte[]?> _outgoing = Channel.CreateUnbounded<byte[]?>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _final = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _completed = new();
    private Task? _startTask, _readerTask, _writerTask;
    private CancellationTokenRegistration _callerCancellation;
    private CancellationToken _callerToken;
    private string? _committedItem;
    private int _pendingBytes, _audioBytes;
    private bool _ending, _disposed;

    internal RealtimeSession(string apiKey, DictationOptions options, IRealtimeTransport? transport = null)
    {
        OpenAiClient.ValidateKey(apiKey); options.Validate();
        _key = apiKey; _options = options; _transport = transport ?? new ClientWebSocketTransport();
    }

    /// <summary>Starts connection setup. Audio may be queued after invoking this, while the returned task is pending.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_startTask is not null) return _startTask.WaitAsync(ct);
            _callerToken = ct;
            _callerCancellation = ct.Register(() => _lifetime.Cancel());
            _startTask = StartCoreAsync();
            return _startTask;
        }
    }

    private async Task StartCoreAsync()
    {
        try
        {
            using var setup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            setup.CancelAfter(TimeSpan.FromSeconds(12));
            await _transport.ConnectAsync(new Uri("wss://api.openai.com/v1/realtime?intent=transcription"), _key, setup.Token).ConfigureAwait(false);
            _readerTask = ReadLoopAsync();
            await SendJsonAsync(SetupMessage(_options), setup.Token).ConfigureAwait(false);
            await _ready.Task.WaitAsync(setup.Token).ConfigureAwait(false);
            _writerTask = WriteLoopAsync();
        }
        catch (Exception)
        {
            // Reader failure and caller cancellation can race. Classify by the
            // caller/session state, not whichever exception won the ready task.
            var canceled = _callerToken.IsCancellationRequested || _disposed;
            Fail(canceled
                ? new OperationCanceledException(_lifetime.Token)
                : new DictationException("Live transcription could not connect. Please try again."));
            if (canceled) throw new OperationCanceledException(_lifetime.Token);
            throw new DictationException("Live transcription could not connect. Please try again.");
        }
    }

    public Task AppendAsync(ReadOnlyMemory<byte> pcm24kMono16, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_startTask is null) throw new InvalidOperationException("Start the session before sending audio.");
            if (_ending) throw new InvalidOperationException("The recording has already ended.");
            if (_lifetime.IsCancellationRequested) throw new OperationCanceledException(_lifetime.Token);
            if (pcm24kMono16.Length % 2 != 0) throw new ArgumentException("Audio must contain complete 16-bit samples.");
            if (pcm24kMono16.Length == 0) return Task.CompletedTask;
            if ((long)_pendingBytes + pcm24kMono16.Length > MaxPendingBytes || (long)_audioBytes + pcm24kMono16.Length > WaveFile.MaxPcmBytes)
                throw new DictationException("The recording buffer is full. Finish this dictation and try a shorter recording.");
            var data = pcm24kMono16.ToArray();
            Interlocked.Add(ref _pendingBytes, data.Length); _audioBytes += data.Length;
            if (!_outgoing.Writer.TryWrite(data)) throw new DictationException("The transcription connection is closed.");
            return Task.CompletedTask;
        }
    }

    public async Task<string> CompleteAsync(CancellationToken ct = default)
    {
        Task start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            start = _startTask ?? throw new InvalidOperationException("Start the session first.");
            if (!_ending) { _ending = true; _outgoing.Writer.TryWrite(null); _outgoing.Writer.TryComplete(); }
        }
        await start.WaitAsync(ct).ConfigureAwait(false);
        try { return await _final.Task.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            Fail(new DictationException("Live transcription timed out after recording stopped."));
            throw new DictationException("Live transcription timed out after recording stopped.");
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await SendJsonAsync("{\"type\":\"input_audio_buffer.clear\"}", _lifetime.Token).ConfigureAwait(false);
            await foreach (var data in _outgoing.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (data is null) await SendJsonAsync("{\"type\":\"input_audio_buffer.commit\"}", _lifetime.Token).ConfigureAwait(false);
                else
                {
                    try { await SendJsonAsync(JsonSerializer.Serialize(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(data) }), _lifetime.Token).ConfigureAwait(false); }
                    finally { Interlocked.Add(ref _pendingBytes, -data.Length); Array.Clear(data); }
                }
            }
        }
        catch (Exception) { Fail(new DictationException("The live transcription connection was interrupted.")); }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            var buffer = new byte[8192]; using var message = new MemoryStream();
            while (!_lifetime.IsCancellationRequested)
            {
                var frame = await _transport.ReceiveAsync(buffer, _lifetime.Token).ConfigureAwait(false);
                if (frame.MessageType == WebSocketMessageType.Close)
                {
                    if (!_final.Task.IsCompleted) Fail(new DictationException("The live transcription connection closed."));
                    return;
                }
                if (message.Length + frame.Count > MaxEventBytes) throw new InvalidDataException();
                message.Write(buffer, 0, frame.Count);
                if (!frame.EndOfMessage) continue;
                HandleEvent(message.ToArray()); message.SetLength(0);
                if (_final.Task.IsCompleted) return;
            }
        }
        catch (Exception) { Fail(new DictationException("Live transcription returned an invalid or interrupted response.")); }
    }

    internal void HandleEvent(ReadOnlySpan<byte> data)
    {
        using var doc = JsonDocument.Parse(data.ToArray());
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        switch (OpenAiClient.Text(root, "type"))
        {
            case "session.updated": _ready.TrySetResult(); break;
            case "error":
            case "conversation.item.input_audio_transcription.failed":
                Fail(new DictationException("OpenAI rejected the live transcription request. Check Settings and API access.")); break;
            case "input_audio_buffer.committed":
                lock (_gate)
                {
                    if (!_ending) break;
                    _committedItem = OpenAiClient.Text(root, "item_id");
                    if (_completed.TryGetValue(_committedItem, out var text)) Finish(text);
                }
                break;
            case "conversation.item.input_audio_transcription.completed":
                lock (_gate)
                {
                    if (!_ending) break;
                    var id = OpenAiClient.Text(root, "item_id");
                    var text = OpenAiClient.Text(root, "transcript").Trim();
                    if (id.Length == 0) break;
                    if (id == _committedItem) Finish(text);
                    else if (_completed.Count < 16) _completed[id] = text;
                }
                break;
        }
    }

    private void Finish(string text)
    {
        if (text.Length == 0) Fail(new NoSpeechDetectedException());
        else _final.TrySetResult(text);
    }
    private void Fail(Exception error)
    {
        _ready.TrySetException(error); _final.TrySetException(error);
        _lifetime.Cancel(); _outgoing.Writer.TryComplete();
        _transport.Abort();
    }
    private Task SendJsonAsync(string json, CancellationToken ct) => _transport.SendAsync(Encoding.UTF8.GetBytes(json), ct);

    internal static string SetupMessage(DictationOptions options) => JsonSerializer.Serialize(new
    {
        type = "session.update", session = new
        {
            type = "transcription", audio = new { input = new
            {
                format = new { type = "audio/pcm", rate = 24000 }, turn_detection = (object?)null,
                transcription = new { model = options.TranscriptionModel, delay = "low", languages = new[] { "en" }, keywords = options.Keywords, prompt = "Personal messages and everyday dictation." }
            } }
        }
    });

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _callerCancellation.Dispose(); _lifetime.Cancel(); _outgoing.Writer.TryComplete(); _transport.Abort();
        foreach (var task in new[] { _startTask, _readerTask, _writerTask })
            if (task is not null) try { await task.ConfigureAwait(false); } catch (Exception) { }
        while (_outgoing.Reader.TryRead(out var audio)) if (audio is not null) Array.Clear(audio);
        _completed.Clear(); _transport.Dispose(); _lifetime.Dispose();
    }
}

internal interface IRealtimeTransport : IDisposable
{
    Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct);
    Task SendAsync(byte[] data, CancellationToken ct);
    Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken ct);
    void Abort();
}

internal sealed class ClientWebSocketTransport : IRealtimeTransport
{
    private readonly ClientWebSocket _socket = new();
    public async Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct)
    {
        _socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
        await _socket.ConnectAsync(uri, ct).ConfigureAwait(false);
    }
    public Task SendAsync(byte[] data, CancellationToken ct) => _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, ct);
    public Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken ct) => _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
    public void Abort() => _socket.Abort();
    public void Dispose() => _socket.Dispose();
}

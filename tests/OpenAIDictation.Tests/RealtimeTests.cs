using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using OpenAIDictation.Core;

namespace OpenAIDictation.Tests;

public class RealtimeTests
{
    [Fact] public async Task AudioWaitsForAckAndCommitFollowsAllQueuedAudio()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        var start = session.StartAsync();
        await session.AppendAsync(new byte[] { 1, 2 }); await session.AppendAsync(new byte[] { 3, 4 });
        var result = session.CompleteAsync();
        await transport.WaitSent(1); Assert.Single(transport.Sent);
        using var setup = JsonDocument.Parse(transport.Sent.First());
        var input = setup.RootElement.GetProperty("session").GetProperty("audio").GetProperty("input");
        Assert.Equal(JsonValueKind.Null, input.GetProperty("turn_detection").ValueKind);
        Assert.Equal(24000, input.GetProperty("format").GetProperty("rate").GetInt32());
        transport.Feed("{\"type\":\"session.updated\"}"); await start; await transport.WaitSent(5);
        Assert.Equal(new[] { "session.update", "input_audio_buffer.clear", "input_audio_buffer.append", "input_audio_buffer.append", "input_audio_buffer.commit" }, transport.Types);
        using var first = JsonDocument.Parse(transport.Sent.ElementAt(2)); Assert.Equal(new byte[] { 1, 2 }, Convert.FromBase64String(first.RootElement.GetProperty("audio").GetString()!));
        transport.Feed("{\"type\":\"input_audio_buffer.committed\",\"item_id\":\"turn\"}");
        transport.Feed("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"item_id\":\"turn\",\"transcript\":\" Hello. \"}");
        Assert.Equal("Hello.", await result.WaitAsync(TimeSpan.FromSeconds(2)));
    }
    [Fact] public async Task WrongTurnFinalCannotCompleteAndBinaryFragmentedFramesWork()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        var start = session.StartAsync(); transport.Feed("{\"type\":\"session.updated\"}"); await start;
        var result = session.CompleteAsync(); await transport.WaitSent(3);
        transport.Feed("{\"type\":\"input_audio_buffer.committed\",\"item_id\":\"right\"}");
        transport.Feed("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"item_id\":\"wrong\",\"transcript\":\"Wrong\"}");
        transport.Feed("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"item_id\":\"right\",\"transcript\":\"Right\"}", WebSocketMessageType.Binary, 7);
        Assert.Equal("Right", await result.WaitAsync(TimeSpan.FromSeconds(2)));
    }
    [Fact] public async Task EarlyFinalWaitsForMatchingCommitAcknowledgment()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        var start = session.StartAsync(); transport.Feed("{\"type\":\"session.updated\"}"); await start;
        var result = session.CompleteAsync();
        transport.Feed("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"item_id\":\"a\",\"transcript\":\"Complete\"}");
        transport.Feed("{\"type\":\"input_audio_buffer.committed\",\"item_id\":\"a\"}");
        Assert.Equal("Complete", await result.WaitAsync(TimeSpan.FromSeconds(2)));
    }
    [Fact] public async Task DuplicateStopCommitsOnlyOnceAndRejectsLateAudio()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        var start = session.StartAsync(); transport.Feed("{\"type\":\"session.updated\"}"); await start;
        var first = session.CompleteAsync(); var second = session.CompleteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AppendAsync(new byte[2]));
        await transport.WaitSent(3); Assert.Equal(1, transport.Types.Count(t => t == "input_audio_buffer.commit"));
        transport.Feed("{\"type\":\"input_audio_buffer.committed\",\"item_id\":\"a\"}");
        transport.Feed("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"item_id\":\"a\",\"transcript\":\"OK\"}");
        Assert.Equal("OK", await first); Assert.Equal("OK", await second);
    }
    [Fact] public async Task BufferIsBoundedBeforeSocketIsReady()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        _ = session.StartAsync(); await session.AppendAsync(new byte[RealtimeSession.MaxPendingBytes]);
        await Assert.ThrowsAsync<DictationException>(() => session.AppendAsync(new byte[2]));
    }
    [Fact] public async Task IncompletePcmIsRejectedBeforeEnqueue()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        _ = session.StartAsync(); await Assert.ThrowsAsync<ArgumentException>(() => session.AppendAsync(new byte[1]));
    }
    [Theory] [InlineData("not-json")] [InlineData("[]")]
    [InlineData("{\"type\":\"error\",\"error\":{\"message\":\"private-provider-body test-secret\"}}")]
    public async Task BadEventsFailWithoutProviderDetails(string message)
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test-secret", new(), transport);
        var start = session.StartAsync(); transport.Feed(message);
        var exception = await Assert.ThrowsAsync<DictationException>(() => start);
        Assert.DoesNotContain("test-secret", exception.ToString()); Assert.DoesNotContain("private-provider-body", exception.ToString());
    }
    [Fact] public async Task ClosedSocketBeforeFinalDoesNotHang()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        var start = session.StartAsync(); transport.Feed("{\"type\":\"session.updated\"}"); await start;
        var result = session.CompleteAsync(); transport.Close();
        await Assert.ThrowsAsync<DictationException>(() => result.WaitAsync(TimeSpan.FromSeconds(2)));
    }
    [Fact] public async Task OversizedEventsFailClosed()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        var start = session.StartAsync(); transport.Feed(new string('x', RealtimeSession.MaxEventBytes + 1), fragment: 4096);
        await Assert.ThrowsAsync<DictationException>(() => start.WaitAsync(TimeSpan.FromSeconds(2)));
    }
    [Fact] public async Task CancellationStopsSetupWithoutHanging()
    {
        using var transport = new FakeTransport(); await using var session = new RealtimeSession("test", new(), transport);
        using var cancellation = new CancellationTokenSource();
        var start = session.StartAsync(cancellation.Token); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TimeSpan.FromSeconds(2)));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task SetupFailureRaceUsesCallerCancellationState(bool cancelCaller)
    {
        using var transport = new FakeTransport();
        var setupSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.SendOverride = async () => { setupSent.TrySetResult(); await resumeSend.Task; };
        transport.OnAbort = () => readerFailed.TrySetResult();
        using var cancellation = new CancellationTokenSource();
        await using var session = new RealtimeSession("test", new(), transport);
        var start = session.StartAsync(cancellation.Token);
        try
        {
            await setupSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
            transport.Feed("{\"type\":\"error\"}");
            // Force the reader to fault _ready first, then optionally cancel the
            // caller before StartCoreAsync observes that provider exception.
            await readerFailed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (cancelCaller) cancellation.Cancel();
        }
        finally { resumeSend.TrySetResult(); }
        if (cancelCaller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TimeSpan.FromSeconds(2)));
        else await Assert.ThrowsAsync<DictationException>(() => start.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private sealed class FakeTransport : IRealtimeTransport
    {
        private readonly Channel<(byte[] Data, WebSocketMessageType Type, bool End)> _frames = Channel.CreateUnbounded<(byte[], WebSocketMessageType, bool)>();
        private readonly SemaphoreSlim _sentSignal = new(0);
        public ConcurrentQueue<string> Sent { get; } = new();
        public Func<Task>? SendOverride { get; set; }
        public Action? OnAbort { get; set; }
        public string[] Types => Sent.Select(s => JsonDocument.Parse(s).RootElement.GetProperty("type").GetString()!).ToArray();
        public Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct) { Assert.Equal("api.openai.com", uri.Host); return Task.CompletedTask; }
        public Task SendAsync(byte[] data, CancellationToken ct) { Sent.Enqueue(Encoding.UTF8.GetString(data)); _sentSignal.Release(); return SendOverride?.Invoke() ?? Task.CompletedTask; }
        public async Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken ct)
        {
            var frame = await _frames.Reader.ReadAsync(ct); frame.Data.CopyTo(buffer, 0); return new(frame.Data.Length, frame.Type, frame.End);
        }
        public void Feed(string json, WebSocketMessageType type = WebSocketMessageType.Text, int fragment = 4096)
        {
            var data = Encoding.UTF8.GetBytes(json);
            for (var i = 0; i < data.Length; i += fragment) _frames.Writer.TryWrite((data[i..Math.Min(data.Length, i + fragment)], type, i + fragment >= data.Length));
        }
        public void Close() => _frames.Writer.TryWrite(([], WebSocketMessageType.Close, true));
        public async Task WaitSent(int count)
        { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); while (Sent.Count < count) await _sentSignal.WaitAsync(timeout.Token); }
        public void Abort() { OnAbort?.Invoke(); }
        public void Dispose() { }
    }
}

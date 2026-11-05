using System.Buffers.Binary;
using System.Net;
using System.Text;
using OpenAIDictation.Core;

namespace OpenAIDictation.Tests;

public class FallbackChunkTests
{
    [Theory] [InlineData(480)] [InlineData(20_000_000)] [InlineData(WaveFile.MaxPcmBytes)]
    public void ChunkFilesStayUnderLimitAndPreserveEverySample(int length)
    {
        var pcm = new byte[length]; Array.Fill<byte>(pcm, 0x40);
        var chunks = WaveFile.UploadChunks(pcm);
        Assert.Equal(length, chunks.Sum(c => c.Length));
        int offset = 0;
        foreach (var chunk in chunks)
        {
            var wave = WaveFile.Create(chunk.Span);
            Assert.InRange(wave.Length, 46, WaveFile.MaxUploadFileBytes);
            Assert.Equal(0, chunk.Length % 2);
            Assert.Equal(chunk.Length, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40)));
            Assert.True(pcm.AsSpan(offset, chunk.Length).SequenceEqual(wave.AsSpan(44)));
            offset += chunk.Length;
        }
    }
    [Fact] public void BoundaryPrefersNearbySilenceWithoutLosingSamples()
    {
        var pcm = new byte[WaveFile.MaxUploadFileBytes + 48000]; Array.Fill<byte>(pcm, 0x40);
        int quietAt = WaveFile.MaxUploadFileBytes - 44 - 960;
        Array.Clear(pcm, quietAt, 960);
        var chunks = WaveFile.UploadChunks(pcm);
        Assert.Equal(quietAt + 480, chunks[0].Length);
        Assert.Equal(pcm.Length, chunks.Sum(c => c.Length));
        Assert.True(chunks[0].Span.SequenceEqual(pcm.AsSpan(0, chunks[0].Length)));
        Assert.True(chunks[1].Span.SequenceEqual(pcm.AsSpan(chunks[0].Length)));
    }
    [Fact] public async Task LongFallbackUploadsSequentiallyAndJoinsTranscriptsInOrder()
    {
        int calls = 0;
        using var client = new OpenAiClient(new HttpClient(new CoreTests.StubHandler(async (request, ct) =>
        {
            int sequence = ++calls;
            var content = Assert.IsType<MultipartFormDataContent>(request.Content);
            var file = content.Single(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            var bytes = await file.ReadAsByteArrayAsync(ct);
            Assert.True(bytes.Length <= WaveFile.MaxUploadFileBytes);
            Assert.Equal(24000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)));
            Assert.Equal(bytes.Length - 44, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)));
            return CoreTests.Json(sequence == 1 ? "{\"text\":\" First part. \"}" : "{\"text\":\" Second part. \"}");
        })));
        var result = await client.TranscribeAsync(new byte[WaveFile.MaxPcmBytes], "test", new());
        Assert.Equal(2, calls); Assert.Equal("First part. Second part.", result);
    }
    [Fact] public async Task CancellationAfterFirstChunkPreventsSecondUpload()
    {
        int calls = 0; using var cts = new CancellationTokenSource();
        using var client = new OpenAiClient(new HttpClient(new CoreTests.StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new CancelOnDisposeContent("{\"text\":\"first\"}", cts) });
        })));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.TranscribeAsync(new byte[WaveFile.MaxPcmBytes], "test", new(), cts.Token));
        Assert.Equal(1, calls);
    }
    [Fact] public async Task FailedLaterChunkDoesNotReturnAnIncompleteTranscriptAsSuccess()
    {
        int calls = 0;
        using var client = new OpenAiClient(new HttpClient(new CoreTests.StubHandler((_, _) => Task.FromResult(++calls == 1
            ? CoreTests.Json("{\"text\":\"first part\"}") : new HttpResponseMessage(HttpStatusCode.InternalServerError)))));
        await Assert.ThrowsAsync<DictationException>(() => client.TranscribeAsync(new byte[WaveFile.MaxPcmBytes], "test", new()));
        Assert.Equal(2, calls);
    }
    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{}")]
    [InlineData(false, "{\"text\":null}")]
    [InlineData(true, "{\"text\":null}")]
    [InlineData(false, "{\"text\":123}")]
    [InlineData(true, "{\"text\":123}")]
    [InlineData(false, "{\"text\":[]}")]
    [InlineData(true, "{\"text\":[]}")]
    public async Task MalformedChunkCannotBeSilentlySkipped(bool afterValidChunk, string malformed)
    {
        int calls = 0;
        using var client = new OpenAiClient(new HttpClient(new CoreTests.StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(CoreTests.Json(afterValidChunk && calls == 1 ? "{\"text\":\"first part\"}" : malformed));
        })));
        var audio = new byte[afterValidChunk ? WaveFile.MaxUploadFileBytes + 2 : 2];
        var error = await Assert.ThrowsAsync<DictationException>(() => client.TranscribeAsync(audio, "test", new()));
        Assert.Equal("OpenAI returned an unreadable transcription response.", error.Message);
        Assert.Equal(afterValidChunk ? 2 : 1, calls);
    }
    [Theory] [InlineData("")] [InlineData("   ")]
    public async Task ExplicitEmptyStringRemainsValidForASilentChunk(string silence)
    {
        int calls = 0;
        using var client = new OpenAiClient(new HttpClient(new CoreTests.StubHandler((_, _) =>
            Task.FromResult(CoreTests.Json(++calls == 1
                ? System.Text.Json.JsonSerializer.Serialize(new { text = silence })
                : "{\"text\":\"second part\"}")))));
        Assert.Equal("second part", await client.TranscribeAsync(new byte[WaveFile.MaxUploadFileBytes + 2], "test", new()));
        Assert.Equal(2, calls);
    }
    [Theory] [InlineData("")] [InlineData("   ")]
    public async Task SilentRecordingHasDistinctDismissalOutcome(string silence)
    {
        int calls=0;
        using var client=new OpenAiClient(new HttpClient(new CoreTests.StubHandler((_,_)=>
        {
            calls++;
            return Task.FromResult(CoreTests.Json(System.Text.Json.JsonSerializer.Serialize(new{text=silence})));
        })));
        await Assert.ThrowsAsync<NoSpeechDetectedException>(()=>client.TranscribeAsync(new byte[2],"test",new()));
        Assert.Equal(1,calls);
        await Assert.ThrowsAsync<NoSpeechDetectedException>(()=>client.CleanupAsync(silence,"test",new()));
        Assert.Equal(1,calls);
    }
    private sealed class CancelOnDisposeContent(string value, CancellationTokenSource source) : StringContent(value, Encoding.UTF8, "application/json")
    {
        protected override void Dispose(bool disposing) { if (disposing) source.Cancel(); base.Dispose(disposing); }
    }
}

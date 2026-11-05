using System.Buffers.Binary;
using System.Text;

namespace OpenAIDictation.Core;

public static class WaveFile
{
    public const int SampleRate = 24000;
    public const int MaxPcmBytes = SampleRate * 2 * 600;
    // Leave ample room below the transcription API's 25 MB file limit.
    public const int MaxUploadFileBytes = 20_000_000;
    internal static IReadOnlyList<ReadOnlyMemory<byte>> UploadChunks(ReadOnlyMemory<byte> pcm)
    {
        Validate(pcm.Length);
        var chunks = new List<ReadOnlyMemory<byte>>();
        int offset = 0;
        while (offset < pcm.Length)
        {
            int count = Math.Min(MaxUploadFileBytes - 44, pcm.Length - offset);
            if (count < pcm.Length - offset) count = QuietBoundary(pcm.Span.Slice(offset, count));
            chunks.Add(pcm.Slice(offset, count)); offset += count;
        }
        return chunks;
    }
    private static int QuietBoundary(ReadOnlySpan<byte> candidate)
    {
        // Prefer the nearest quiet 20 ms window in the last half second. The
        // slices remain contiguous: no samples are discarded or duplicated.
        const int window = SampleRate * 2 / 50;
        int first = Math.Max(0, candidate.Length - SampleRate);
        for (int start = candidate.Length - window; start >= first; start -= window)
        {
            long energy = 0;
            for (int i = start; i < start + window; i += 2)
            {
                int sample = BinaryPrimitives.ReadInt16LittleEndian(candidate.Slice(i, 2));
                energy += (long)sample * sample;
            }
            if (energy / (window / 2) <= 200L * 200) return start + window / 2;
        }
        return candidate.Length;
    }
    private static void Validate(int length)
    {
        if (length == 0 || length % 2 != 0 || length > MaxPcmBytes)
            throw new ArgumentException("Recording must contain 16-bit PCM and be at most ten minutes long.");
    }
    public static byte[] Create(ReadOnlySpan<byte> pcm)
    {
        Validate(pcm.Length);
        var wave = new byte[44 + pcm.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wave, 0); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), pcm.Length + 36);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wave, 8); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), SampleRate); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(wave, 36); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), pcm.Length);
        pcm.CopyTo(wave.AsSpan(44)); return wave;
    }
}

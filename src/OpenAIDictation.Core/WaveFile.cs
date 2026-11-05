using System.Buffers.Binary;
using System.Text;

namespace OpenAIDictation.Core;

public static class WaveFile
{
    public const int SampleRate = 24000;
    public const int MaxPcmBytes = SampleRate * 2 * 600;
    public static byte[] Create(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length == 0 || pcm.Length % 2 != 0 || pcm.Length > MaxPcmBytes)
            throw new ArgumentException("Recording must contain 16-bit PCM and be at most ten minutes long.");
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

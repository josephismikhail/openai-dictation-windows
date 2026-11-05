using OpenAIDictation.Core;

namespace OpenAIDictation.Windows;

internal static class PcmLimit
{
    internal static int AcceptedBytes(long recorded,int incoming)
    {
        if(recorded<0 || recorded>WaveFile.MaxPcmBytes || (recorded&1)!=0 || incoming<0)
            throw new ArgumentOutOfRangeException(nameof(recorded));
        return (int)Math.Min(incoming,WaveFile.MaxPcmBytes-recorded)&~1;
    }
}

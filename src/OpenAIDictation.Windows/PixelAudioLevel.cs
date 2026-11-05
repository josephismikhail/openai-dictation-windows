namespace OpenAIDictation.Windows;

/// <summary>Same PCM16 perceptual level mapping used by the Pixel's voice view.</summary>
internal static class PixelAudioLevel
{
    internal static float Measure(ReadOnlySpan<byte> pcm)
    {
        int end=pcm.Length&~1;
        if(end==0)return 0;
        double squares=0;
        for(int i=0;i<end;i+=2)
        {
            double sample=(short)(pcm[i]|pcm[i+1]<<8)/32768d;
            squares+=sample*sample;
        }
        double rms=Math.Sqrt(squares/(end/2));
        double db=20*Math.Log10(Math.Max(rms,.000001));
        return (float)Math.Clamp((db+55)/43,0,1);
    }
}

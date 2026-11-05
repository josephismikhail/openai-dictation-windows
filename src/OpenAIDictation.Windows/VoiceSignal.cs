namespace OpenAIDictation.Windows;

/// <summary>Speech-driven envelope with strong gain, quick attack and a short trailing history.</summary>
internal sealed class VoiceSignal
{
    internal const int BarCount=9;
    internal const double QuietHeight=2.5,PeakHeight=32;
    private static readonly double[] Shape=[.38,.58,.78,.94,1,.94,.78,.58,.38];
    private readonly double[] history=new double[BarCount];
    private double target,envelope,historyTime;

    internal void Observe(float level)
    {
        if(!float.IsFinite(level) || level<=.018f){target=0;return;}
        target=Math.Sqrt(1-Math.Exp(-22*Math.Min(1,level-.018)));
    }
    internal void Advance(double seconds)
    {
        if(!double.IsFinite(seconds) || seconds<=0)return;
        seconds=Math.Min(seconds,.1);
        double tau=target>envelope?.028:.12;
        envelope+=(target-envelope)*(1-Math.Exp(-seconds/tau));
        historyTime+=seconds;
        while(historyTime>=.035)
        {
            Array.Copy(history,1,history,0,history.Length-1);history[^1]=envelope;historyTime-=.035;
        }
    }
    internal double Height(int bar)
    {
        if((uint)bar>=BarCount)throw new ArgumentOutOfRangeException(nameof(bar));
        return QuietHeight+(PeakHeight-QuietHeight)*Shape[bar]*Math.Clamp(.85*envelope+.15*history[bar],0,1);
    }
    internal void Reset(){target=envelope=historyTime=0;Array.Clear(history);}
}

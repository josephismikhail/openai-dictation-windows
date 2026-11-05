namespace OpenAIDictation.Windows;

internal readonly record struct PixelVoiceBar(double X,double Height);
internal readonly record struct PixelVoiceShape(double Radius,double Exponent,double Rotation,int Alpha);
internal sealed record PixelVoiceFrame(double Width,double StrokeWidth,int WaveAlpha,bool FlatLine,PixelVoiceBar[] Bars,PixelVoiceShape? OuterShape,PixelVoiceShape? InnerShape);

/// <summary>Port of the phone's VoicePanel animation, with an explicit clock for deterministic checks.</summary>
internal sealed class PixelVoiceAnimation
{
    internal const int BarCount=49;
    private readonly double[] levels=new double[BarCount];
    private double displayed,target,lastFrame,lastSample,processingStarted;
    private bool processing;
    internal void Clear()
    {
        Array.Clear(levels);displayed=target=lastFrame=lastSample=processingStarted=0;processing=false;
    }
    internal void Observe(float value,double milliseconds)
    {
        if(!float.IsFinite(value))value=0;
        target=Math.Pow(Math.Clamp((value-.18)/.45,0,1),.4);
        lastSample=milliseconds;
        Array.Copy(levels,1,levels,0,levels.Length-1);levels[^1]=target;
    }
    internal void BeginProcessing(double milliseconds)
    {
        if(!processing)processingStarted=milliseconds;
        processing=true;
    }
    internal PixelVoiceFrame Frame(double milliseconds,double viewWidth,double viewHeight,bool animate)
    {
        double width=Math.Max(1,Math.Min(346,viewWidth-40));
        if(processing)
        {
            double transition=animate?Math.Clamp((milliseconds-processingStarted)/450,0,1):1;
            transition=transition*transition*(3-2*transition);
            double seconds=animate?Math.Max(0,milliseconds-processingStarted)/1000:0;
            double breath=.5+.5*Math.Sin(seconds*3.2);
            double radius=Math.Min(60+5*breath,Math.Min(viewWidth,viewHeight)*.23)*(.65+.35*transition);
            lastFrame=milliseconds;
            return new(width,1.2+2*displayed,(int)((190+65*displayed)*(1-transition)),false,
                transition<1?Bars(width*(1-.8*transition),viewHeight,1-transition):[],
                new(radius*1.22,3.5,0,(int)(12*transition)),
                new(radius,3.5+Math.Sin(seconds*1.4)*.6,Math.Sin(seconds*.9)*.1,(int)(220*transition)));
        }
        double dt=lastFrame==0?.016:Math.Clamp((milliseconds-lastFrame)/1000,0,.1);
        double desired=milliseconds-lastSample>220?0:target;
        displayed+=(desired-displayed)*(1-Math.Exp(-dt*(desired>displayed?32:14)));
        lastFrame=milliseconds;
        bool flat=displayed<.02;
        return new(width,flat?1:1.2+2*displayed,flat?128:(int)(190+65*displayed),flat,
            flat?[]:Bars(width,viewHeight,1),null,null);
    }
    private PixelVoiceBar[] Bars(double width,double viewHeight,double scale)
    {
        var bars=new PixelVoiceBar[BarCount];
        for(int i=0;i<bars.Length;i++)
        {
            double position=i/(double)(BarCount-1),taper=Math.Sin(position*Math.PI);
            // Java Math.round uses floor(x+.5), rather than .NET's midpoint-to-even.
            int age=(int)Math.Floor(Math.Abs(2*position-1)*(BarCount/2)+.5);
            double history=levels[BarCount-1-age],energy=displayed*(.55+.45*history);
            double height=1+Math.Min(300,viewHeight*.36)*taper*energy;
            bars[i]=new(-width/2+position*width,height*scale);
        }
        return bars;
    }
}

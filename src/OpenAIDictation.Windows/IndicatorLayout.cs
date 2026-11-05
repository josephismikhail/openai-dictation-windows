namespace OpenAIDictation.Windows;

internal readonly record struct IndicatorBounds(int Left,int Top,int Width,int Height);

/// <summary>Positions the visible indicator using physical monitor/work-area coordinates.</summary>
internal static class IndicatorLayout
{
    internal const double WindowWidth=320,WindowHeight=60,TaskbarGap=20;
    internal const double PillWidth=300,PillHeight=50,PillTop=5,CancelTargetWidth=54;
    internal const double ErrorWidth=480,ErrorMinimumHeight=110;

    internal static IndicatorAction HitTest(double x,double y,double windowWidth,OverlayState state,double windowHeight=WindowHeight)
    {
        double width=state==OverlayState.Error?ErrorWidth:PillWidth,left=(windowWidth-width)/2;
        double bottom=state==OverlayState.Error?windowHeight-PillTop:PillTop+PillHeight;
        if(x<left || x>left+width || y<PillTop || y>bottom)return IndicatorAction.None;
        return state!=OverlayState.Recording || x>=left+PillWidth-CancelTargetWidth
            ?IndicatorAction.Cancel:IndicatorAction.Stop;
    }

    internal static IndicatorBounds Calculate(int monitorLeft,int monitorRight,int workTop,int workBottom,uint dpiX,uint dpiY,OverlayState state=OverlayState.Recording,double errorHeight=ErrorMinimumHeight)
    {
        if(monitorRight<=monitorLeft || workBottom<=workTop || dpiX==0 || dpiY==0)
            throw new ArgumentOutOfRangeException(nameof(monitorRight));
        double scaleX=dpiX/96d,scaleY=dpiY/96d;
        double logicalWidth=state==OverlayState.Error?ErrorWidth+20:WindowWidth;
        double logicalHeight=state==OverlayState.Error?Math.Max(ErrorMinimumHeight,errorHeight)+10:WindowHeight;
        int width=(int)Math.Round(logicalWidth*scaleX),height=(int)Math.Round(logicalHeight*scaleY);
        int left=(int)Math.Round(monitorLeft+((long)monitorRight-monitorLeft-width)/2d);
        int top=Math.Max(workTop,workBottom-(int)Math.Round(TaskbarGap*scaleY)-height);
        return new(left,top,width,height);
    }
}

internal enum IndicatorAction { None,Stop,Cancel }

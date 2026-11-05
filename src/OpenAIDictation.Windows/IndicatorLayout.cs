namespace OpenAIDictation.Windows;

internal readonly record struct IndicatorBounds(int Left,int Top,int Width,int Height);

/// <summary>Positions the visible indicator using physical monitor/work-area coordinates.</summary>
internal static class IndicatorLayout
{
    internal const double WindowWidth=320,WindowHeight=78,TaskbarGap=8;
    internal const double PillWidth=300,PillHeight=68,PillTop=5,CancelTargetWidth=54;

    internal static IndicatorAction HitTest(double x,double y,double windowWidth,OverlayState state)
    {
        double left=(windowWidth-PillWidth)/2;
        if(x<left || x>left+PillWidth || y<PillTop || y>PillTop+PillHeight)return IndicatorAction.None;
        return state!=OverlayState.Recording || x>=left+PillWidth-CancelTargetWidth
            ?IndicatorAction.Cancel:IndicatorAction.Stop;
    }

    internal static IndicatorBounds Calculate(int monitorLeft,int monitorRight,int workTop,int workBottom,uint dpiX,uint dpiY)
    {
        if(monitorRight<=monitorLeft || workBottom<=workTop || dpiX==0 || dpiY==0)
            throw new ArgumentOutOfRangeException(nameof(monitorRight));
        double scaleX=dpiX/96d,scaleY=dpiY/96d;
        int width=(int)Math.Round(WindowWidth*scaleX),height=(int)Math.Round(WindowHeight*scaleY);
        int left=(int)Math.Round(monitorLeft+((long)monitorRight-monitorLeft-width)/2d);
        int top=Math.Max(workTop,workBottom-(int)Math.Round(TaskbarGap*scaleY)-height);
        return new(left,top,width,height);
    }
}

internal enum IndicatorAction { None,Stop,Cancel }

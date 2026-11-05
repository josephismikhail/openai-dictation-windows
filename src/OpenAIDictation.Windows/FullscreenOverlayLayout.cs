namespace OpenAIDictation.Windows;

internal readonly record struct FullscreenOverlayBounds(int Left,int Top,int Width,int Height,double LogicalWidth,double LogicalHeight);

/// <summary>Uses the complete selected monitor, including its taskbar, with physical placement and DIP drawing.</summary>
internal static class FullscreenOverlayLayout
{
    internal static FullscreenOverlayBounds Calculate(int left,int top,int right,int bottom,uint dpiX,uint dpiY)
    {
        if(right<=left || bottom<=top || dpiX==0 || dpiY==0)throw new ArgumentOutOfRangeException(nameof(right));
        int width=checked(right-left),height=checked(bottom-top);
        return new(left,top,width,height,width*96d/dpiX,height*96d/dpiY);
    }
    internal static bool StopsRecording(OverlayState state)=>state==OverlayState.Recording;
}

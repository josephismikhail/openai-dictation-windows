using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;

namespace OpenAIDictation.Windows;

internal enum OverlayState { Recording, Processing, Error }
internal sealed class OverlayWindow:Window
{
    private readonly Indicator indicator=new();
    private readonly DispatcherTimer timer;
    private long lastTick;
    public event Action? StopRequested;
    public event Action? CancelRequested;
    public OverlayWindow()
    {
        Title="OpenAI Dictation Indicator";
        Width=IndicatorLayout.WindowWidth;Height=IndicatorLayout.WindowHeight;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;Topmost=true;ShowActivated=false;ShowInTaskbar=false;Content=indicator;
        SourceInitialized+=(_,_)=>{nint handle=new WindowInteropHelper(this).Handle;NativeMethods.SetWindowLongPtr(handle,-20,NativeMethods.GetWindowLongPtr(handle,-20)|0x08000000|0x80);};
        MouseLeftButtonUp+=(_,e)=>
        {
            var point=e.GetPosition(this);
            var action=IndicatorLayout.HitTest(point.X,point.Y,ActualWidth,indicator.State);
            if(action==IndicatorAction.Cancel)CancelRequested?.Invoke();else if(action==IndicatorAction.Stop)StopRequested?.Invoke();
        };
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
        timer.Tick+=(_,_)=>
        {
            long now=Stopwatch.GetTimestamp();double elapsed=Stopwatch.GetElapsedTime(lastTick,now).TotalSeconds;lastTick=now;
            indicator.Time+=elapsed;indicator.Signal.Advance(elapsed);indicator.InvalidateVisual();
        };
        IsVisibleChanged+=(_,_)=>{if(IsVisible){lastTick=Stopwatch.GetTimestamp();timer.Start();}else timer.Stop();};
    }
    public void Present(OverlayState state,nint target,string? message=null)
    {
        indicator.State=state;indicator.Message=message??"";indicator.Signal.Reset();
        var monitor=NativeMethods.MonitorFromWindow(target,2);var info=new NativeMethods.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>()};
        if(!NativeMethods.GetMonitorInfo(monitor,ref info))throw new InvalidOperationException("The display could not be located.");
        uint dpiX=96,dpiY=96;NativeMethods.GetDpiForMonitor(monitor,0,out dpiX,out dpiY);
        var bounds=IndicatorLayout.Calculate(info.Monitor.Left,info.Monitor.Right,info.Work.Top,info.Work.Bottom,dpiX==0?96:dpiX,dpiY==0?96:dpiY);
        nint handle=new WindowInteropHelper(this).EnsureHandle();
        Place(handle,bounds);Show();Place(handle,bounds);
        indicator.InvalidateVisual();
    }
    private static void Place(nint handle,IndicatorBounds bounds)=>NativeMethods.SetWindowPos(handle,0,bounds.Left,bounds.Top,bounds.Width,bounds.Height,0x0010|0x0004);
    public void SetLevel(float value)=>indicator.Signal.Observe(value);
    private sealed class Indicator:FrameworkElement
    {
        internal OverlayState State;internal double Time;internal string Message="";
        internal readonly VoiceSignal Signal=new();
        private static readonly Brush Surface=Solid(Color.FromArgb(252,12,12,12));
        private static readonly Brush CancelSurface=Solid(Color.FromRgb(42,42,42));
        private static readonly Pen Border=Line(Solid(Color.FromRgb(72,72,72)),1);
        private static readonly Pen CancelLine=Line(Brushes.White,1.6);
        private static readonly Pen SpinnerLine=Line(Brushes.White,2);
        private static Brush Solid(Color color){var value=new SolidColorBrush(color);value.Freeze();return value;}
        private static Pen Line(Brush brush,double thickness){var value=new Pen(brush,thickness);value.Freeze();return value;}
        protected override void OnRender(DrawingContext dc)
        {
            double width=IndicatorLayout.PillWidth,left=(ActualWidth-width)/2;
            double centerY=IndicatorLayout.PillTop+IndicatorLayout.PillHeight/2;
            dc.DrawRoundedRectangle(Surface,Border,new Rect(left,IndicatorLayout.PillTop,width,IndicatorLayout.PillHeight),34,34);
            dc.DrawEllipse(Brushes.White,null,new Point(left+27,centerY),4,4);
            if(State==OverlayState.Recording)
            {
                double first=left+(width-(VoiceSignal.BarCount*6+(VoiceSignal.BarCount-1)*5))/2;
                for(int i=0;i<VoiceSignal.BarCount;i++){double height=Signal.Height(i);dc.DrawRoundedRectangle(Brushes.White,null,new Rect(first+i*11,centerY-height/2,6,height),3,3);}
            }
            else
            {
                if(State==OverlayState.Processing)
                {
                    dc.PushTransform(new RotateTransform(Time*260,left+82,centerY));
                    var geometry=new StreamGeometry();using(var path=geometry.Open()){path.BeginFigure(new Point(left+82,centerY-10),false,false);path.ArcTo(new Point(left+72,centerY),new Size(10,10),0,true,SweepDirection.Clockwise,true,false);}
                    dc.DrawGeometry(null,SpinnerLine,geometry);dc.Pop();
                }
                var text=new FormattedText(State==OverlayState.Error?"Dictation error":"Processing",System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),15,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new Point(left+(State==OverlayState.Error?87:102),centerY-text.Height/2));
            }
            double cx=left+width-27;dc.DrawEllipse(CancelSurface,null,new Point(cx,centerY),16,16);
            dc.DrawLine(CancelLine,new Point(cx-4,centerY-4),new Point(cx+4,centerY+4));dc.DrawLine(CancelLine,new Point(cx+4,centerY-4),new Point(cx-4,centerY+4));
        }
    }
}

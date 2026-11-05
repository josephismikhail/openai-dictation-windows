using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenAIDictation.Windows;

internal enum OverlayState { Recording, Processing, Error }
internal sealed class OverlayWindow:Window
{
    private readonly Indicator indicator=new();
    private readonly DispatcherTimer timer;
    public event Action? StopRequested;
    public event Action? CancelRequested;
    public OverlayWindow()
    {
        Title="OpenAI Dictation Indicator";
        Width=266;Height=50;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;Topmost=true;ShowActivated=false;ShowInTaskbar=false;Content=indicator;
        SourceInitialized+=(_,_)=>{nint handle=new WindowInteropHelper(this).Handle;NativeMethods.SetWindowLongPtr(handle,-20,NativeMethods.GetWindowLongPtr(handle,-20)|0x08000000|0x80);};
        MouseLeftButtonUp+=(_,e)=>
        {
            double width=indicator.State==OverlayState.Recording?172:216;
            if(e.GetPosition(this).X>(ActualWidth-width)/2+width-35 || indicator.State!=OverlayState.Recording)CancelRequested?.Invoke();else StopRequested?.Invoke();
        };
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
        timer.Tick+=(_,_)=>{indicator.Time+=.033;indicator.InvalidateVisual();};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)timer.Start();else timer.Stop();};
    }
    public void Present(OverlayState state,nint target,string? message=null)
    {
        indicator.State=state;indicator.Message=message??"";indicator.Level=0;
        var monitor=NativeMethods.MonitorFromWindow(target,2);var info=new NativeMethods.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>()};
        NativeMethods.GetMonitorInfo(monitor,ref info);Show();
        uint dpiX=96,dpiY=96;NativeMethods.GetDpiForMonitor(monitor,0,out dpiX,out dpiY);
        double scaleX=Math.Max(96,dpiX)/96.0,scaleY=Math.Max(96,dpiY)/96.0;
        int width=(int)Math.Round(Width*scaleX),height=(int)Math.Round(Height*scaleY);
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle,0,(info.Monitor.Left+info.Monitor.Right-width)/2,info.Monitor.Bottom-(int)Math.Round(40*scaleY)-height,width,height,0x0010|0x0004);
        indicator.InvalidateVisual();
    }
    public void SetLevel(float value)=>indicator.Level=value;
    private sealed class Indicator:FrameworkElement
    {
        internal OverlayState State;internal float Level;internal double Time;internal string Message="";
        private static readonly Brush Pink=new SolidColorBrush(Color.FromRgb(242,140,187));
        protected override void OnRender(DrawingContext dc)
        {
            double width=State==OverlayState.Recording?172:216,left=(ActualWidth-width)/2;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(250,35,35,38)),new Pen(new SolidColorBrush(Color.FromRgb(73,73,78)),1),new Rect(left,5,width,40),24,24);
            dc.DrawEllipse(State==OverlayState.Error?Brushes.IndianRed:Pink,null,new Point(left+17,25),3.5,3.5);
            if(State==OverlayState.Recording)
            {
                for(int i=0;i<9;i++){double height=3+15*Math.Min(1,Level*(.55+.45*Math.Sin(Time*13+i*.9)));dc.DrawRoundedRectangle(Pink,null,new Rect(left+53+i*7,25-height/2,4,height),2,2);}
            }
            else
            {
                if(State==OverlayState.Processing)
                {
                    dc.PushTransform(new RotateTransform(Time*260,left+37,25));
                    var geometry=new StreamGeometry();using(var path=geometry.Open()){path.BeginFigure(new Point(left+37,18.5),false,false);path.ArcTo(new Point(left+30.5,25),new Size(6.5,6.5),0,true,SweepDirection.Clockwise,true,false);}
                    dc.DrawGeometry(null,new Pen(Pink,1.6),geometry);dc.Pop();
                }
                var text=new FormattedText(State==OverlayState.Error?"Dictation error":"Processing",System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.Gainsboro,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new Point(left+51,17));
            }
            double cx=left+width-19;dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(56,56,60)),null,new Point(cx,25),11,11);
            var pen=new Pen(Brushes.LightGray,1.2);dc.DrawLine(pen,new Point(cx-3,22),new Point(cx+3,28));dc.DrawLine(pen,new Point(cx+3,22),new Point(cx-3,28));
        }
    }
}

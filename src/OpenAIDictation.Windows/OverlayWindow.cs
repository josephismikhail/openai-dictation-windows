using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenAIDictation.Windows;

internal enum OverlayState { Recording, Processing, Error }
internal sealed class OverlayWindow:Window
{
    private readonly Indicator indicator=new();
    private readonly DispatcherTimer timer;
    private FullscreenOverlayBounds? selectedMonitor;
    public event Action? StopRequested;
    public event Action? CancelRequested;
    public OverlayWindow()
    {
        Title="OpenAI Dictation";
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;Topmost=true;ShowActivated=false;ShowInTaskbar=false;Focusable=false;Content=indicator;
        SourceInitialized+=(_,_)=>
        {
            nint handle=new WindowInteropHelper(this).Handle;
            var source=HwndSource.FromHwnd(handle);
            NativeMethods.SetWindowLongPtr(handle,-20,NativeMethods.GetWindowLongPtr(handle,-20)|0x08000000|0x80);
            source?.AddHook((nint hwnd,int message,nint wp,nint lp,ref bool handled)=>
            {
                // Clicking must preserve the original editor's focus and selection.
                if(message==0x21){handled=true;return 3;} // WM_MOUSEACTIVATE: MA_NOACTIVATE
                return 0;
            });
        };
        MouseLeftButtonUp+=(_,_)=>
        {
            if(FullscreenOverlayLayout.StopsRecording(indicator.State))StopRequested?.Invoke();
            else if(indicator.State==OverlayState.Error)CancelRequested?.Invoke();
        };
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
        timer.Tick+=(_,_)=>{if(SystemParameters.ClientAreaAnimation)indicator.InvalidateVisual();};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)timer.Start();else timer.Stop();};
    }
    public void Present(OverlayState state,nint target,string? message=null)
    {
        // Freeze the recording monitor across processing, even if the editor moves.
        if(state!=OverlayState.Processing || selectedMonitor==null)
        {
            var monitor=NativeMethods.MonitorFromWindow(target,2);
            var info=new NativeMethods.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>()};
            if(!NativeMethods.GetMonitorInfo(monitor,ref info))throw new InvalidOperationException("The display could not be located.");
            uint dpiX=96,dpiY=96;NativeMethods.GetDpiForMonitor(monitor,0,out dpiX,out dpiY);
            selectedMonitor=FullscreenOverlayLayout.Calculate(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right,info.Monitor.Bottom,dpiX==0?96:dpiX,dpiY==0?96:dpiY);
        }
        var bounds=selectedMonitor.Value;
        indicator.SetState(state,message);
        Width=bounds.LogicalWidth;Height=bounds.LogicalHeight;
        nint handle=new WindowInteropHelper(this).EnsureHandle();
        Place(handle,bounds);Show();Place(handle,bounds);indicator.InvalidateVisual();
    }
    private static void Place(nint handle,FullscreenOverlayBounds bounds)=>NativeMethods.SetWindowPos(handle,-1,bounds.Left,bounds.Top,bounds.Width,bounds.Height,0x0010);
    public void SetLevel(float value){indicator.Signal.Observe(value,Now());indicator.InvalidateVisual();}
    private static double Now()=>Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency;
    internal sealed class Indicator:FrameworkElement
    {
        internal OverlayState State;
        internal string Message="";
        internal readonly PixelVoiceAnimation Signal=new();
        private static readonly Brush Dim=Solid(Color.FromArgb(204,0,0,0));
        private static readonly Brush ErrorSurface=Solid(Color.FromRgb(18,18,18));
        private static readonly Pen ErrorBorder=FrozenPen(Brushes.DimGray,1);
        internal void SetState(OverlayState state,string? message=null)
        {
            State=state;Message=string.IsNullOrWhiteSpace(message)?"Dictation could not finish. Please try again.":message;
            if(state==OverlayState.Recording)Signal.Clear();else if(state==OverlayState.Processing)Signal.BeginProcessing(Now());
            ToolTip=state==OverlayState.Error?Message:null;
            AutomationProperties.SetName(this,state==OverlayState.Error?"Dictation error. "+Message:state==OverlayState.Recording?"Listening. Click anywhere to finish dictation. Escape cancels.":"Processing dictation. Escape cancels.");
            AutomationProperties.SetLiveSetting(this,state==OverlayState.Error?AutomationLiveSetting.Assertive:AutomationLiveSetting.Polite);
        }
        private static Brush Solid(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
        private static Pen FrozenPen(Brush brush,double width){var pen=new Pen(brush,width);pen.Freeze();return pen;}
        private FormattedText Text(string value,double size)=>new(value,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        private double ErrorWidth=>Math.Max(1,Math.Min(480,ActualWidth-40));
        private FormattedText ErrorText(){var text=Text(Message,14);text.MaxTextWidth=Math.Max(1,ErrorWidth-44);return text;}
        internal double ErrorHeight=>Math.Max(110,ErrorText().Height+68);
        protected override AutomationPeer OnCreateAutomationPeer()=>new IndicatorPeer(this);
        private sealed class IndicatorPeer(Indicator owner):FrameworkElementAutomationPeer(owner)
        {
            protected override string GetClassNameCore()=>"Dictation status";
            protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Text;
            protected override string GetNameCore()=>AutomationProperties.GetName(owner);
            protected override string GetHelpTextCore()=>owner.State==OverlayState.Error?owner.Message:base.GetHelpTextCore();
        }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Dim,null,new Rect(0,0,ActualWidth,ActualHeight));
            double cx=ActualWidth/2,cy=ActualHeight/2;
            if(State==OverlayState.Error)
            {
                double left=cx-ErrorWidth/2,top=cy-ErrorHeight/2;
                dc.DrawRoundedRectangle(ErrorSurface,ErrorBorder,new Rect(left,top,ErrorWidth,ErrorHeight),18,18);
                dc.DrawText(Text("Dictation error",16),new Point(left+22,top+18));
                dc.DrawText(ErrorText(),new Point(left+22,top+51));return;
            }
            var frame=Signal.Frame(Now(),ActualWidth,ActualHeight,SystemParameters.ClientAreaAnimation);
            var waveBrush=Solid(Color.FromArgb((byte)frame.WaveAlpha,255,255,255));
            var pen=new Pen(waveBrush,frame.StrokeWidth){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            if(frame.FlatLine)dc.DrawLine(pen,new Point(cx-frame.Width/2,cy),new Point(cx+frame.Width/2,cy));
            else foreach(var bar in frame.Bars)dc.DrawLine(pen,new Point(cx+bar.X,cy-bar.Height/2),new Point(cx+bar.X,cy+bar.Height/2));
            if(frame.OuterShape is { } outer)DrawShape(dc,cx,cy,outer);
            if(frame.InnerShape is { } inner)DrawShape(dc,cx,cy,inner);
        }
        private static void DrawShape(DrawingContext dc,double cx,double cy,PixelVoiceShape shape)
        {
            var geometry=new StreamGeometry();
            using(var path=geometry.Open())
            {
                double cosRotation=Math.Cos(shape.Rotation),sinRotation=Math.Sin(shape.Rotation);
                for(int i=0;i<=96;i++)
                {
                    double angle=i*Math.PI*2/96,cos=Math.Cos(angle),sin=Math.Sin(angle);
                    double x=shape.Radius*Math.Sign(cos)*Math.Pow(Math.Abs(cos),2/shape.Exponent),y=shape.Radius*Math.Sign(sin)*Math.Pow(Math.Abs(sin),2/shape.Exponent);
                    var point=new Point(cx+x*cosRotation-y*sinRotation,cy+x*sinRotation+y*cosRotation);
                    if(i==0)path.BeginFigure(point,true,true);else path.LineTo(point,true,false);
                }
            }
            geometry.Freeze();dc.DrawGeometry(Solid(Color.FromArgb((byte)shape.Alpha,255,255,255)),null,geometry);
        }
    }
}

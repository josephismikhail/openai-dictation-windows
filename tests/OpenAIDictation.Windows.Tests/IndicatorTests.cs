using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class IndicatorTests
{
    [Theory]
    [InlineData(0,1920,0,1040,96,96)]
    [InlineData(-2560,0,-200,1200,144,144)]
    [InlineData(1920,5760,120,2160,192,192)]
    [InlineData(-1919,0,0,1000,120,144)]
    public void WindowIsCenteredOnFullMonitorAndAboveWorkArea(int left,int right,int top,int bottom,uint dx,uint dy)
    {
        var b=IndicatorLayout.Calculate(left,right,top,bottom,dx,dy);
        Assert.InRange(Math.Abs((b.Left+b.Width/2d)-(left+((long)right-left)/2d)),0,.5);
        Assert.Equal(bottom-(int)Math.Round(IndicatorLayout.TaskbarGap*dy/96d),b.Top+b.Height);
        Assert.Equal((int)Math.Round(IndicatorLayout.WindowWidth*dx/96d),b.Width);
        Assert.Equal((int)Math.Round(IndicatorLayout.WindowHeight*dy/96d),b.Height);
    }
    [Fact] public void WorkAreaNotMonitorBottomDeterminesTaskbarClearance()
    {
        var normal=IndicatorLayout.Calculate(0,1920,0,1040,96,96);
        var tallTaskbar=IndicatorLayout.Calculate(0,1920,0,950,96,96);
        Assert.Equal(90,normal.Top-tallTaskbar.Top);
    }
    [Fact] public void InvalidDisplaysFailWithoutOffscreenCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>IndicatorLayout.Calculate(0,0,0,1040,96,96));
        Assert.Throws<ArgumentOutOfRangeException>(()=>IndicatorLayout.Calculate(0,1920,0,1040,0,96));
    }
    [Fact] public void SilenceRemainsFlatWithoutTimeDrivenFakeSpeech()
    {
        var signal=new VoiceSignal();signal.Observe(.01f);
        for(int i=0;i<90;i++)signal.Advance(1d/60);
        for(int bar=0;bar<VoiceSignal.BarCount;bar++)Assert.Equal(VoiceSignal.QuietHeight,signal.Height(bar));
    }
    [Fact] public void OrdinarySpeechProducesLargePeaksQuickly()
    {
        var signal=new VoiceSignal();signal.Observe(.12f);
        for(int i=0;i<6;i++)signal.Advance(1d/60);
        Assert.InRange(signal.Height(4),33,VoiceSignal.PeakHeight);
        Assert.True(signal.Height(4)>signal.Height(0)*1.7);
        for(int i=0;i<12;i++)signal.Advance(1d/60);
        Assert.InRange(signal.Height(4),38,VoiceSignal.PeakHeight);
    }
    [Fact] public void QuietSpeechStillProducesAnObviousSignal()
    {
        var signal=new VoiceSignal();signal.Observe(.03f);
        for(int i=0;i<6;i++)signal.Advance(1d/60);
        Assert.InRange(signal.Height(4),29,VoiceSignal.PeakHeight);
        Assert.True(signal.Height(4)>VoiceSignal.QuietHeight*11);
    }
    [Fact] public void LargerPillContainsMaximumSignalWithoutClipping()
    {
        Assert.True(IndicatorLayout.WindowWidth>=IndicatorLayout.PillWidth);
        Assert.True(IndicatorLayout.WindowHeight>=IndicatorLayout.PillTop+IndicatorLayout.PillHeight);
        Assert.True(IndicatorLayout.PillHeight-VoiceSignal.PeakHeight>=10);
    }
    [Theory]
    [InlineData(160,39,(int)OverlayState.Recording,(int)IndicatorAction.Stop)]
    [InlineData(283,39,(int)OverlayState.Recording,(int)IndicatorAction.Cancel)]
    [InlineData(257,39,(int)OverlayState.Recording,(int)IndicatorAction.Cancel)]
    [InlineData(160,39,(int)OverlayState.Processing,(int)IndicatorAction.Cancel)]
    [InlineData(160,39,(int)OverlayState.Error,(int)IndicatorAction.Cancel)]
    [InlineData(5,39,(int)OverlayState.Recording,(int)IndicatorAction.None)]
    [InlineData(160,2,(int)OverlayState.Recording,(int)IndicatorAction.None)]
    [InlineData(160,76,(int)OverlayState.Recording,(int)IndicatorAction.None)]
    public void VisiblePillHasCorrectStopAndCancelTargets(double x,double y,int state,int expected)
    {
        Assert.Equal((IndicatorAction)expected,IndicatorLayout.HitTest(x,y,IndicatorLayout.WindowWidth,(OverlayState)state));
    }
    [Fact] public void ErrorCardExpandsWithoutMovingOffCenterOrOntoTaskbar()
    {
        var bounds=IndicatorLayout.Calculate(-2560,0,-200,1200,144,144,OverlayState.Error,150);
        Assert.Equal(750,bounds.Width);Assert.Equal(240,bounds.Height);
        Assert.Equal(-1280,bounds.Left+bounds.Width/2);
        Assert.Equal(1188,bounds.Top+bounds.Height);
        Assert.Equal(IndicatorAction.Cancel,IndicatorLayout.HitTest(250,149,500,OverlayState.Error,160));
        Assert.Equal(IndicatorAction.None,IndicatorLayout.HitTest(250,158,500,OverlayState.Error,160));
    }
    [Fact] public void FullSafeErrorIsWrappedAndAccessibleWithoutOpeningAWindow()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                string message=string.Join(" ",Enumerable.Repeat("Check your microphone connection and try recording again.",8));
                var indicator=new OverlayWindow.Indicator();indicator.SetState(OverlayState.Error,message);
                Assert.Equal(message,indicator.ToolTip);
                Assert.True(indicator.ErrorHeight>IndicatorLayout.ErrorMinimumHeight);
                var peer=System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(indicator);
                Assert.NotNull(peer);Assert.Contains(message,peer.GetName());Assert.Equal(message,peer.GetHelpText());
                indicator.SetState(OverlayState.Recording);
                Assert.Null(indicator.ToolTip);Assert.DoesNotContain(message,peer.GetName());
            }
            catch(Exception caught){error=caught;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact] public void PausingSmoothlyReturnsToTheQuietLine()
    {
        var signal=new VoiceSignal();signal.Observe(.2f);
        for(int i=0;i<30;i++)signal.Advance(1d/60);
        double high=signal.Height(4);signal.Observe(0);signal.Advance(1d/60);
        Assert.InRange(signal.Height(4),VoiceSignal.QuietHeight+1,high);
        for(int i=0;i<60;i++)signal.Advance(1d/60);
        for(int bar=0;bar<VoiceSignal.BarCount;bar++)Assert.InRange(signal.Height(bar),VoiceSignal.QuietHeight,VoiceSignal.QuietHeight+.1);
    }
    [Fact] public void NonfiniteInputAndClockValuesCannotCorruptTheDrawing()
    {
        var signal=new VoiceSignal();signal.Observe(float.NaN);signal.Advance(double.NaN);signal.Advance(-1);
        Assert.Equal(VoiceSignal.QuietHeight,signal.Height(4));
        signal.Observe(float.MaxValue);for(int i=0;i<100;i++)signal.Advance(.016);
        Assert.InRange(signal.Height(4),VoiceSignal.QuietHeight,VoiceSignal.PeakHeight);
        signal.Reset();Assert.Equal(VoiceSignal.QuietHeight,signal.Height(4));
    }
}

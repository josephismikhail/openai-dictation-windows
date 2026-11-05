using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class PixelVoiceAnimationTests
{
    [Theory]
    [InlineData(0,0,1920,1080,96,96,1920,1080)]
    [InlineData(-2560,-200,0,1240,144,144,2560/1.5,960)]
    [InlineData(1920,120,5760,2280,192,192,1920,1080)]
    [InlineData(-1920,0,0,1200,120,144,1536,800)]
    public void CoversExactlyOneFullPhysicalMonitor(int left,int top,int right,int bottom,uint dx,uint dy,double logicalWidth,double logicalHeight)
    {
        var bounds=FullscreenOverlayLayout.Calculate(left,top,right,bottom,dx,dy);
        Assert.Equal(left,bounds.Left);Assert.Equal(top,bounds.Top);
        Assert.Equal(right-left,bounds.Width);Assert.Equal(bottom-top,bounds.Height);
        Assert.Equal(logicalWidth,bounds.LogicalWidth,6);Assert.Equal(logicalHeight,bounds.LogicalHeight,6);
        Assert.Equal((left+(double)right)/2,bounds.Left+bounds.LogicalWidth*dx/192);
        Assert.Equal((top+(double)bottom)/2,bounds.Top+bounds.LogicalHeight*dy/192);
    }
    [Fact] public void InvalidGeometryIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>FullscreenOverlayLayout.Calculate(0,0,0,100,96,96));
        Assert.Throws<ArgumentOutOfRangeException>(()=>FullscreenOverlayLayout.Calculate(0,0,100,100,0,96));
    }
    [Fact] public void OnlyRecordingClickStopsTheSession()
    {
        Assert.True(FullscreenOverlayLayout.StopsRecording(OverlayState.Recording));
        Assert.False(FullscreenOverlayLayout.StopsRecording(OverlayState.Processing));
        Assert.False(FullscreenOverlayLayout.StopsRecording(OverlayState.Error));
    }
    [Fact] public void SilenceIsAFlatCenteredLineWithNoSyntheticSpeech()
    {
        var animation=new PixelVoiceAnimation();animation.Observe(.17f,1000);
        var frame=animation.Frame(1016,1920,1080,true);
        Assert.True(frame.FlatLine);Assert.Empty(frame.Bars);Assert.Equal(346,frame.Width);
        Assert.Equal(1,frame.StrokeWidth);Assert.Equal(128,frame.WaveAlpha);
    }
    [Fact] public void PhoneGainAndSmoothingProduce49StrongCenteredBars()
    {
        var animation=new PixelVoiceAnimation();animation.Observe(.5f,1000);
        var frame=animation.Frame(1016,1920,1080,true);
        Assert.False(frame.FlatLine);Assert.Equal(49,frame.Bars.Length);
        Assert.Equal(-173,frame.Bars[0].X);Assert.Equal(0,frame.Bars[24].X);Assert.Equal(173,frame.Bars[^1].X);
        Assert.InRange(frame.Bars[24].Height,95,105);
        Assert.Equal(1,frame.Bars[0].Height,6);Assert.Equal(1,frame.Bars[^1].Height,6);
        Assert.InRange(frame.StrokeWidth,1.8,2);Assert.InRange(frame.WaveAlpha,210,220);
    }
    [Fact] public void MissingSamplesDecayAfter220MillisecondsAndClearResetsHistory()
    {
        var animation=new PixelVoiceAnimation();animation.Observe(1,1000);
        for(int i=0;i<12;i++)animation.Frame(1016+i*16,1920,1080,true);
        Assert.False(animation.Frame(1220,1920,1080,true).FlatLine);
        for(int i=0;i<50;i++)animation.Frame(1236+i*16,1920,1080,true);
        Assert.True(animation.Frame(2052,1920,1080,true).FlatLine);
        animation.Clear();Assert.True(animation.Frame(3000,1920,1080,true).FlatLine);
    }
    [Fact] public void ProcessingContractsTheExistingWaveThenBreathesIndeterminately()
    {
        var animation=new PixelVoiceAnimation();animation.Observe(.5f,1000);
        var listening=animation.Frame(1016,1920,1080,true);animation.BeginProcessing(1100);
        var halfway=animation.Frame(1325,1920,1080,true);
        Assert.Equal(listening.Bars[24].Height/2,halfway.Bars[24].Height,6);
        Assert.Equal(-103.8,halfway.Bars[0].X,6);Assert.InRange(halfway.InnerShape!.Value.Alpha,109,110);
        var complete=animation.Frame(1550,1920,1080,true);
        Assert.Empty(complete.Bars);Assert.Equal(220,complete.InnerShape!.Value.Alpha);
        Assert.NotEqual(complete.InnerShape.Value.Radius,animation.Frame(2050,1920,1080,true).InnerShape!.Value.Radius);
        Assert.NotNull(animation.Frame(100_000,1920,1080,true).InnerShape);
    }
    [Fact] public void ReducedMotionSkipsContractionAndKeepsTheProcessingShapeStill()
    {
        var animation=new PixelVoiceAnimation();animation.BeginProcessing(1000);
        var first=animation.Frame(1000,1920,1080,false);var later=animation.Frame(8000,1920,1080,false);
        Assert.Empty(first.Bars);Assert.Equal(first.InnerShape,later.InnerShape);
        Assert.Equal(62.5,first.InnerShape!.Value.Radius);Assert.Equal(3.5,first.InnerShape.Value.Exponent);
        Assert.Equal(0,first.InnerShape.Value.Rotation);Assert.Equal(220,first.InnerShape.Value.Alpha);
    }
    [Fact] public void NarrowDisplaysStillContainTheWaveAndShape()
    {
        var animation=new PixelVoiceAnimation();animation.Observe(1,1000);
        var wave=animation.Frame(1016,280,400,true);Assert.Equal(240,wave.Width);
        animation.BeginProcessing(1100);
        Assert.True(animation.Frame(1600,280,400,true).OuterShape!.Value.Radius<140);
    }
    [Fact] public void NonfiniteLevelsDoNotCorruptTheVisual()
    {
        var animation=new PixelVoiceAnimation();animation.Observe(float.NaN,1000);
        Assert.True(animation.Frame(1016,1920,1080,true).FlatLine);
    }
    [Fact] public void FullSafeErrorsRemainAccessibleWithoutOpeningAWindow()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                string message=string.Join(" ",Enumerable.Repeat("Check your microphone connection and try again.",8));
                var indicator=new OverlayWindow.Indicator();indicator.Measure(new System.Windows.Size(1920,1080));indicator.Arrange(new System.Windows.Rect(0,0,1920,1080));
                indicator.SetState(OverlayState.Error,message);
                Assert.Equal(message,indicator.ToolTip);Assert.True(indicator.ErrorHeight>110);
                var peer=System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(indicator);
                Assert.NotNull(peer);Assert.Contains(message,peer.GetName());Assert.Equal(message,peer.GetHelpText());
                indicator.SetState(OverlayState.Recording);Assert.Null(indicator.ToolTip);Assert.DoesNotContain(message,peer.GetName());
            }
            catch(Exception caught){error=caught;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}

using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class RecordingStartCueTests
{
    private sealed class Output(string id,bool muted=false):IAudioOutput
    {
        public string Id=>id;
        public bool IsMuted{get;private set;}=muted;
        internal readonly List<bool> Changes=[];
        internal bool FailRemute;
        public void SetMuted(bool value)
        {
            if(value && FailRemute)throw new InvalidOperationException("Simulated unplug");
            IsMuted=value;Changes.Add(value);
        }
        public void Dispose(){}
    }
    private sealed class Source(params Output[] outputs):IAudioOutputSource
    {public IReadOnlyList<IAudioOutput> GetActiveOutputs()=>outputs;}
    private sealed class Cleanup(Action action):IDisposable
    {public void Dispose()=>action();}
    private sealed class Cue(Output speaker):IRecordingCueOutput,IRecordingCuePlatform
    {
        internal readonly List<string> Events=[];
        internal Action? Protected,Playing;
        internal Action? NotificationFailure;
        internal bool ProtectionFails,PlaybackFails,Opened,OtherSessionsMuted;
        internal readonly ManualResetEventSlim PlayingEvent=new();
        public string Id=>speaker.Id;
        public IRecordingCueOutput OpenDefaultOutput(){Opened=true;Events.Add("open");return this;}
        public IDisposable ProtectOtherSessions(Action failed)
        {
            Assert.True(speaker.IsMuted);Events.Add("protect");
            if(ProtectionFails)throw new InvalidOperationException();
            NotificationFailure=failed;OtherSessionsMuted=true;Protected?.Invoke();
            return new Cleanup(()=>{Events.Add("restore sessions");OtherSessionsMuted=false;});
        }
        public void Play(CancellationToken token)
        {
            Assert.False(speaker.IsMuted);Assert.True(OtherSessionsMuted);
            Events.Add("play");PlayingEvent.Set();Playing?.Invoke();
            if(PlaybackFails)throw new InvalidOperationException();
            token.ThrowIfCancellationRequested();
        }
        public void Dispose()=>Events.Add("close output");
    }
    [Fact] public async Task CueOnlyOpensItsEndpointWithOtherSessionsProtected()
    {
        var speaker=new Output("speaker");var headphones=new Output("headphones");
        using var mute=SystemAudioMute.Acquire(new Source(speaker,headphones));var output=new Cue(speaker);
        output.Playing=()=>{Assert.True(headphones.IsMuted);mute.Refresh();Assert.False(speaker.IsMuted);};
        using var cue=new RecordingStartCue(mute,output,CancellationToken.None);await cue.Completion;
        Assert.True(speaker.IsMuted);Assert.True(headphones.IsMuted);Assert.False(output.OtherSessionsMuted);
        Assert.Equal(new[]{true,false,true},speaker.Changes);
        Assert.Equal(new[]{"open","protect","play","close output","restore sessions"},output.Events);
    }
    [Fact] public async Task UserMutedOutputNeverOpensOrPlays()
    {
        var speaker=new Output("speaker",true);using var mute=SystemAudioMute.Acquire(new Source(speaker));var output=new Cue(speaker);
        using var cue=new RecordingStartCue(mute,output,CancellationToken.None);await cue.Completion;
        Assert.DoesNotContain("play",output.Events);Assert.True(speaker.IsMuted);Assert.Empty(speaker.Changes);
    }
    [Fact] public async Task ProtectionFailureNeverUnmutesOutput()
    {
        var speaker=new Output("speaker");using var mute=SystemAudioMute.Acquire(new Source(speaker));var output=new Cue(speaker){ProtectionFails=true};
        using var cue=new RecordingStartCue(mute,output,CancellationToken.None);await cue.Completion;
        Assert.DoesNotContain("play",output.Events);Assert.True(speaker.IsMuted);Assert.Equal(new[]{true},speaker.Changes);
    }
    [Fact] public async Task PlaybackFailureRemutesBeforeRestoringSessions()
    {
        var speaker=new Output("speaker");using var mute=SystemAudioMute.Acquire(new Source(speaker));var output=new Cue(speaker){PlaybackFails=true};
        using var cue=new RecordingStartCue(mute,output,CancellationToken.None);await cue.Completion;
        Assert.True(speaker.IsMuted);Assert.False(output.OtherSessionsMuted);Assert.Equal(new[]{true,false,true},speaker.Changes);
    }
    [Fact] public async Task PreCancelledCueNeverOpensAnyAudio()
    {
        var speaker=new Output("speaker");using var mute=SystemAudioMute.Acquire(new Source(speaker));var output=new Cue(speaker);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        using var cue=new RecordingStartCue(mute,output,cancel.Token);await cue.Completion;
        Assert.False(output.Opened);Assert.True(speaker.IsMuted);
    }
    [Fact] public async Task CancellationDuringProtectionNeverUnmutesOutput()
    {
        var speaker=new Output("speaker");using var mute=SystemAudioMute.Acquire(new Source(speaker));var output=new Cue(speaker);
        using var cancel=new CancellationTokenSource();output.Protected=cancel.Cancel;
        using var cue=new RecordingStartCue(mute,output,cancel.Token);await cue.Completion;
        Assert.DoesNotContain("play",output.Events);Assert.True(speaker.IsMuted);Assert.False(output.OtherSessionsMuted);
    }
    [Fact] public void StopWaitsForCueToRemuteBeforeEndpointLeaseRestores()
    {
        var speaker=new Output("speaker");using var mute=SystemAudioMute.Acquire(new Source(speaker));var output=new Cue(speaker);
        using var cancel=new CancellationTokenSource();output.Playing=()=>cancel.Token.WaitHandle.WaitOne(3000);
        var cue=new RecordingStartCue(mute,output,cancel.Token);
        Assert.True(output.PlayingEvent.Wait(3000));cancel.Cancel();cue.Dispose();cue.Dispose();
        Assert.True(speaker.IsMuted);Assert.False(output.OtherSessionsMuted);
        mute.Dispose();Assert.False(speaker.IsMuted);Assert.Equal(new[]{true,false,true,false},speaker.Changes);
    }
    [Fact] public async Task NewSessionProtectionFailureImmediatelyRemutesAndSignalsCaptureFailure()
    {
        var speaker=new Output("speaker");using var mute=SystemAudioMute.Acquire(new Source(speaker));var output=new Cue(speaker);
        int failures=0;mute.Failed+=()=>failures++;
        output.Playing=()=>{output.NotificationFailure!();Assert.True(speaker.IsMuted);};
        using var cue=new RecordingStartCue(mute,output,CancellationToken.None);await cue.Completion;
        Assert.Equal(1,failures);Assert.False(output.OtherSessionsMuted);
    }
    [Fact] public async Task DriverRemuteFailureKeepsSessionProtectionUntilCaptureDisposal()
    {
        var speaker=new Output("speaker");using var mute=SystemAudioMute.Acquire(new Source(speaker));var output=new Cue(speaker);
        int failures=0;mute.Failed+=()=>failures++;output.Playing=()=>speaker.FailRemute=true;
        var cue=new RecordingStartCue(mute,output,CancellationToken.None);await cue.Completion;
        Assert.True(output.OtherSessionsMuted);Assert.Equal(1,failures);
        cue.Dispose();Assert.False(output.OtherSessionsMuted);
    }
    [Fact] public void ToneIsAudibleShortStereoAndHasClickFreeEdges()
    {
        float[] tone=StartCueTone.Samples();Assert.Equal(5760*2,tone.Length);
        Assert.Equal(0,tone[0]);Assert.Equal(0,tone[^1]);Assert.InRange(tone.Max(Math.Abs),.30f,.37f);
        double rms=Math.Sqrt(tone.Average(sample=>(double)sample*sample));
        Assert.InRange(rms,.16,.20);
        for(int i=0;i<tone.Length;i+=2)Assert.Equal(tone[i],tone[i+1]);
    }
}

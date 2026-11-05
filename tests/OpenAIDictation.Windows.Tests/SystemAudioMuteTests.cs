using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class SystemAudioMuteTests
{
    private sealed class Device(string id,bool muted=false)
    {
        internal string Id=id;
        internal bool Muted=muted,Unavailable,FailMute;
        internal int Disposals;
        internal readonly List<bool> Changes=[];
    }
    private sealed class Output(Device device):IAudioOutput
    {
        public string Id=>device.Id;
        public bool IsMuted=>device.Unavailable?throw new InvalidOperationException():device.Muted;
        public void SetMuted(bool value)
        {
            if(device.Unavailable)throw new InvalidOperationException();
            device.Muted=value;device.Changes.Add(value);
            if(value && device.FailMute)throw new InvalidOperationException();
        }
        public void Dispose()=>device.Disposals++;
    }
    private sealed class Source(params Device[] devices):IAudioOutputSource
    {
        internal readonly List<Device> Devices=[..devices];
        internal int Enumerations;
        public IReadOnlyList<IAudioOutput> GetActiveOutputs(){Enumerations++;return Devices.Select(d=>(IAudioOutput)new Output(d)).ToArray();}
    }

    [Fact] public void MutesEveryActiveOutputAndRestoresOnlyChangedDevices()
    {
        var speakers=new Device("speakers");var headphones=new Device("headphones");var alreadyMuted=new Device("monitor",true);
        var source=new Source(speakers,headphones,alreadyMuted);
        using(var lease=SystemAudioMute.Acquire(source))
        {Assert.True(speakers.Muted);Assert.True(headphones.Muted);Assert.True(alreadyMuted.Muted);}
        Assert.False(speakers.Muted);Assert.False(headphones.Muted);Assert.True(alreadyMuted.Muted);
        Assert.Equal(new[]{true,false},speakers.Changes);Assert.Empty(alreadyMuted.Changes);
        Assert.Equal(1,alreadyMuted.Disposals);
    }
    [Fact] public void NewlyConnectedOutputIsMutedAndRestoredWithTheSameLease()
    {
        var first=new Device("first");var second=new Device("new output");var source=new Source(first);
        using(var lease=SystemAudioMute.Acquire(source))
        {source.Devices.Add(second);lease.Refresh();Assert.True(second.Muted);}
        Assert.False(first.Muted);Assert.False(second.Muted);
        Assert.Equal(new[]{true,false},first.Changes);Assert.Equal(new[]{true,false},second.Changes);
        Assert.Equal(2,first.Disposals);
    }
    [Fact] public void PartialAcquisitionFailureRollsBackAllChangedOutputs()
    {
        var first=new Device("first");var failed=new Device("failed"){FailMute=true};var untouched=new Device("last");
        Assert.Throws<InvalidOperationException>(()=>SystemAudioMute.Acquire(new Source(first,failed,untouched)));
        Assert.False(first.Muted);Assert.False(failed.Muted);Assert.False(untouched.Muted);
        Assert.Equal(new[]{true,false},first.Changes);Assert.Equal(new[]{true,false},failed.Changes);
        Assert.Equal(1,untouched.Disposals);
    }
    [Fact] public void UnpluggedDeviceDoesNotPreventOtherOutputsFromRestoring()
    {
        var unplugged=new Device("unplugged");var normal=new Device("normal");var lease=SystemAudioMute.Acquire(new Source(unplugged,normal));
        unplugged.Unavailable=true;lease.Dispose();
        Assert.False(normal.Muted);Assert.Equal(1,unplugged.Disposals);Assert.Equal(1,normal.Disposals);
    }
    [Fact] public void DisposalIsIdempotentAndStopsFurtherDiscovery()
    {
        var device=new Device("speaker");var source=new Source(device);var lease=SystemAudioMute.Acquire(source);
        lease.Dispose();lease.Dispose();lease.Refresh();
        Assert.Equal(1,source.Enumerations);Assert.Equal(1,device.Disposals);Assert.Equal(new[]{true,false},device.Changes);
    }
    [Fact] public void PreservesAnExplicitExternalUnmuteAtRestore()
    {
        var device=new Device("speaker");var lease=SystemAudioMute.Acquire(new Source(device));device.Muted=false;lease.Dispose();
        Assert.False(device.Muted);Assert.Equal(new[]{true},device.Changes);
    }
    [Fact] public void RefreshKeepsOutputsMutedDuringTheRecording()
    {
        var device=new Device("speaker");using(var lease=SystemAudioMute.Acquire(new Source(device)))
        {device.Muted=false;lease.Refresh();Assert.True(device.Muted);}
        Assert.False(device.Muted);Assert.Equal(new[]{true,true,false},device.Changes);
    }
    [Fact] public void SupportsMachinesWithNoActiveRenderEndpoint()
    {
        using var lease=SystemAudioMute.Acquire(new Source());lease.Refresh();
    }
}

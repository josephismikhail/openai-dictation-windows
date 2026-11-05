using System.Runtime.InteropServices;

namespace OpenAIDictation.Windows;

/// <summary>A recording-scoped mute lease. Never changes device volume levels.</summary>
internal sealed class SystemAudioMute : IDisposable
{
    private readonly object gate=new();
    private readonly IAudioOutputSource source;
    private readonly Dictionary<string,IAudioOutput> changed=new(StringComparer.Ordinal);
    private Timer? monitor;
    private string? cueOutput;
    private bool disposed;
    internal event Action? Failed;

    internal static SystemAudioMute Acquire()=>new(new CoreAudioOutputSource(),true);
    internal static SystemAudioMute Acquire(IAudioOutputSource source,bool monitorChanges=false)=>new(source,monitorChanges);
    private SystemAudioMute(IAudioOutputSource source,bool monitorChanges)
    {
        this.source=source;
        try
        {
            Refresh();
            if(monitorChanges)monitor=new Timer(_=>{try{Refresh();}catch{try{Failed?.Invoke();}catch{}}},null,250,250);
        }
        catch
        {
            Dispose();
            throw new InvalidOperationException("Windows could not mute audio for recording. Please try again.");
        }
    }
    /// <summary>Also catches newly connected or newly active output devices while listening.</summary>
    internal void Refresh()
    {
        lock(gate)
        {
            if(disposed)return;
            var outputs=source.GetActiveOutputs();
            int next=0;
            try
            {
                for(;next<outputs.Count;next++)
                {
                    var output=outputs[next];bool retain=false;
                    try
                    {
                        if(changed.ContainsKey(output.Id))
                        {
                            // Use the latest handle after a device reconnects, while
                            // preserving ownership of our original unmuted state.
                            if(output.Id!=cueOutput && !output.IsMuted)output.SetMuted(true);
                            if(!ReferenceEquals(changed[output.Id],output))changed[output.Id].Dispose();
                            changed[output.Id]=output;retain=true;
                        }
                        else if(!output.IsMuted)
                        {
                            changed.Add(output.Id,output);retain=true;output.SetMuted(true);
                        }
                    }
                    finally{if(!retain)output.Dispose();}
                }
            }
            finally{for(int i=next+1;i<outputs.Count;i++)outputs[i].Dispose();}
        }
    }
    /// <summary>Permit only an output this lease muted, after the caller has silenced its other sessions.</summary>
    internal IDisposable? PermitCue(string id)
    {
        lock(gate)
        {
            if(disposed || cueOutput!=null || !changed.TryGetValue(id,out var output) || !output.IsMuted)return null;
            cueOutput=id;
            try{output.SetMuted(false);return new CuePermit(this,id);}
            catch{try{EndCue(id);}catch{}throw;}
        }
    }
    private void EndCue(string id)
    {
        lock(gate)
        {
            if(cueOutput!=id)return;
            // Re-mute before the caller restores any other application's session.
            if(!disposed && changed.TryGetValue(id,out var output))output.SetMuted(true);
            cueOutput=null;
        }
    }
    internal void CloseCue(){lock(gate){if(cueOutput!=null)EndCue(cueOutput);}}
    private sealed class CuePermit(SystemAudioMute owner,string id):IDisposable
    {
        public void Dispose()=>owner.EndCue(id);
    }
    internal void ReportCueFailure(){try{Failed?.Invoke();}catch{}}
    public void Dispose()
    {
        lock(gate)
        {
            if(disposed)return;disposed=true;monitor?.Dispose();monitor=null;
            foreach(var output in changed.Values)
            {
                // A disconnected device must not prevent other devices restoring.
                // Leave a user's subsequent explicit unmute alone.
                try{if(output.IsMuted)output.SetMuted(false);}catch{}
                finally{try{output.Dispose();}catch{}}
            }
            changed.Clear();
        }
    }
}

internal interface IAudioOutputSource
{
    IReadOnlyList<IAudioOutput> GetActiveOutputs();
}
internal interface IAudioOutput : IDisposable
{
    string Id {get;}
    bool IsMuted {get;}
    void SetMuted(bool value);
}

/// <summary>Enumerates every active render endpoint, not only the default speaker.</summary>
internal sealed class CoreAudioOutputSource : IAudioOutputSource
{
    public IReadOnlyList<IAudioOutput> GetActiveOutputs()
    {
        var result=new List<IAudioOutput>();IMMDeviceEnumerator? enumerator=null;IMMDeviceCollection? collection=null;
        try
        {
            enumerator=(IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0,1,out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out uint count));
            for(uint i=0;i<count;i++)
            {
                IMMDevice? device=null;object? volume=null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i,out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    Guid iid=typeof(IAudioEndpointVolume).GUID;
                    Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,0,out volume));
                    result.Add(new CoreAudioOutput(id,(IAudioEndpointVolume)volume));volume=null;
                }
                finally{Release(volume);Release(device);}
            }
            return result;
        }
        catch{foreach(var item in result)item.Dispose();throw;}
        finally{Release(collection);Release(enumerator);}
    }
    private static void Release(object? value){if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    private sealed class CoreAudioOutput(string id,IAudioEndpointVolume volume):IAudioOutput
    {
        private IAudioEndpointVolume? endpoint=volume;
        public string Id=>id;
        public bool IsMuted
        {
            get{ObjectDisposedException.ThrowIf(endpoint==null,this);Marshal.ThrowExceptionForHR(endpoint.GetMute(out bool muted));return muted;}
        }
        public void SetMuted(bool value)
        {
            ObjectDisposedException.ThrowIf(endpoint==null,this);Guid context=new("4c7db7b4-765d-4e77-9514-79b9a012faba");
            Marshal.ThrowExceptionForHR(endpoint.SetMute(value,ref context));
        }
        public void Dispose(){var old=endpoint;endpoint=null;Release(old);}
    }
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private sealed class MMDeviceEnumerator {}
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]int EnumAudioEndpoints(int dataFlow,uint stateMask,out IMMDeviceCollection devices);
    }
    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]int GetCount(out uint count);
        [PreserveSig]int Item(uint index,out IMMDevice device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]int Activate(ref Guid iid,uint context,nint parameters,[MarshalAs(UnmanagedType.IUnknown)]out object instance);
        [PreserveSig]int OpenPropertyStore(int access,out nint properties);
        [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetState(out uint state);
    }
    [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]int RegisterControlChangeNotify(nint callback);
        [PreserveSig]int UnregisterControlChangeNotify(nint callback);
        [PreserveSig]int GetChannelCount(out uint count);
        [PreserveSig]int SetMasterVolumeLevel(float level,ref Guid context);
        [PreserveSig]int SetMasterVolumeLevelScalar(float level,ref Guid context);
        [PreserveSig]int GetMasterVolumeLevel(out float level);
        [PreserveSig]int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig]int SetChannelVolumeLevel(uint channel,float level,ref Guid context);
        [PreserveSig]int SetChannelVolumeLevelScalar(uint channel,float level,ref Guid context);
        [PreserveSig]int GetChannelVolumeLevel(uint channel,out float level);
        [PreserveSig]int GetChannelVolumeLevelScalar(uint channel,out float level);
        [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);
        [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
    }
}

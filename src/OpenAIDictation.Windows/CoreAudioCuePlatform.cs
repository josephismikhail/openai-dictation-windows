using System.Runtime.InteropServices;
using IMMDeviceEnumerator=OpenAIDictation.Windows.CoreAudioDevices.IDeviceEnumerator;
using IMMDevice=OpenAIDictation.Windows.CoreAudioDevices.IDevice;

namespace OpenAIDictation.Windows;

/// <summary>One explicitly selected WASAPI endpoint; no default-device rerouting during the cue.</summary>
internal sealed class CoreAudioCuePlatform : IRecordingCuePlatform
{
    public IRecordingCueOutput OpenDefaultOutput()=>new Output();
    private sealed class Output : IRecordingCueOutput
    {
        private IMMDevice? device;
        private IAudioClient? audio;
        private IAudioRenderClient? render;
        public string Id {get;private set;}="";
        internal Output()
        {
            IMMDeviceEnumerator? enumerator=null;
            try
            {
                enumerator=CoreAudioDevices.CreateEnumerator();
                Check(enumerator.GetDefaultAudioEndpoint(0,1,out device));
                Check(device.GetId(out string id));Id=id;
                audio=Activate<IAudioClient>(device);
                var format=new WaveFormat{Tag=3,Channels=2,Rate=48000,BytesPerSecond=384000,BlockAlign=8,Bits=32};
                // Shared mode with Windows' format conversion. Fails safely if an exclusive owner prevents mixing.
                Check(audio.Initialize(0,0x88000000,2_000_000,0,ref format,0));
                Guid renderId=typeof(IAudioRenderClient).GUID;
                Check(audio.GetService(ref renderId,out object renderer));render=(IAudioRenderClient)renderer;
            }
            catch{Dispose();throw;}
            finally{Release(enumerator);}
        }
        public IDisposable ProtectOtherSessions(Action failed)
        {
            ObjectDisposedException.ThrowIf(device==null,this);
            return new SessionGuard(Activate<IAudioSessionManager2>(device),failed);
        }
        public void Play(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(audio==null || render==null,this);
            float[] samples=StartCueTone.Samples();uint frames=(uint)samples.Length/2;
            Check(audio.GetBufferSize(out uint available));
            if(frames>available)throw new InvalidOperationException("The output buffer is too small for the cue.");
            token.ThrowIfCancellationRequested();
            Check(render.GetBuffer(frames,out nint buffer));
            try{Marshal.Copy(samples,0,buffer,samples.Length);}
            finally{Check(render.ReleaseBuffer(frames,0));}
            Check(audio.Start());
            try
            {
                var timeout=System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    if(token.WaitHandle.WaitOne(10))token.ThrowIfCancellationRequested();
                    Check(audio.GetCurrentPadding(out uint remaining));
                    if(remaining==0)return;
                }while(timeout.ElapsedMilliseconds<1000);
                throw new InvalidOperationException("The audio cue did not finish.");
            }
            finally{audio.Stop();audio.Reset();}
        }
        public void Dispose(){Release(render);render=null;Release(audio);audio=null;Release(device);device=null;}
    }

    /// <summary>Muting sessions is only needed for the short audible cue; endpoint mute owns the rest.</summary>
    private sealed class SessionGuard : IDisposable
    {
        private readonly object gate=new();
        private readonly IAudioSessionManager2 manager;
        private readonly Notification notification;
        private readonly Dictionary<string,Session> changed=new(StringComparer.Ordinal);
        private readonly Action failed;
        private bool disposed,registered;
        internal SessionGuard(IAudioSessionManager2 manager,Action failed)
        {
            this.manager=manager;this.failed=failed;notification=new Notification(OnCreated);
            IAudioSessionEnumerator? sessions=null;
            try
            {
                Check(manager.RegisterSessionNotification(notification));registered=true;
                Check(manager.GetSessionEnumerator(out sessions));
                // GetCount enables notifications; enumerate only after registration to close the startup race.
                Check(sessions.GetCount(out int count));
                for(int i=0;i<count;i++)
                {
                    Check(sessions.GetSession(i,out IAudioSessionControl control));
                    // A retained session may share its RCW with a notification. Do not
                    // forcibly disconnect that RCW with ReleaseComObject; the runtime
                    // releases it once its managed references are no longer retained.
                    Add(control);
                }
            }
            catch{Dispose();throw;}
            finally{Release(sessions);}
        }
        private void OnCreated(IAudioSessionControl control)
        {
            try{Add(control);}catch{try{failed();}catch{}}
        }
        private void Add(IAudioSessionControl control)
        {
            lock(gate)
            {
                if(disposed)return;
                var detail=(IAudioSessionControl2)control;
                Check(detail.GetProcessId(out uint process));
                if(process==Environment.ProcessId)return;
                Check(detail.GetSessionInstanceIdentifier(out string id));
                if(changed.ContainsKey(id))return;
                var volume=(ISimpleAudioVolume)control;
                Check(volume.GetMute(out bool muted));
                if(muted)return;
                // Keep an explicit COM reference beyond OnSessionCreated's borrowed argument.
                var session=new Session(volume,Marshal.GetIUnknownForObject(control));
                changed.Add(id,session);
                Guid context=Guid.Empty;Check(volume.SetMute(true,ref context));
            }
        }
        public void Dispose()
        {
            lock(gate){if(disposed)return;disposed=true;}
            // Never unregister under gate: a callback may be waiting for that lock.
            if(registered){try{manager.UnregisterSessionNotification(notification);}catch{}}
            lock(gate)
            {
                foreach(var session in changed.Values)
                {
                    try
                    {
                        if(session.Volume.GetMute(out bool muted)>=0 && muted)
                        {Guid context=Guid.Empty;session.Volume.SetMute(false,ref context);}
                    }
                    catch{}
                    finally{Marshal.Release(session.Reference);}
                }
                changed.Clear();
            }
            Release(manager);GC.KeepAlive(notification);
        }
        private sealed record Session(ISimpleAudioVolume Volume,nint Reference);
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    private sealed class Notification(Action<IAudioSessionControl> created):IAudioSessionNotification
    {
        public int OnSessionCreated(IAudioSessionControl control){created(control);return 0;}
    }
    private static T Activate<T>(IMMDevice device)
    {
        Guid id=typeof(T).GUID;Check(device.Activate(ref id,23,0,out object result));return (T)result;
    }
    private static void Check(int hr)=>Marshal.ThrowExceptionForHR(hr);
    private static void Release(object? value){if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    [StructLayout(LayoutKind.Sequential,Pack=2)] private struct WaveFormat
    {public ushort Tag,Channels;public uint Rate,BytesPerSecond;public ushort BlockAlign,Bits,Extra;}

    [ComImport,Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]int Initialize(int mode,uint flags,long duration,long period,ref WaveFormat format,nint sessionId);
        [PreserveSig]int GetBufferSize(out uint frames);
        [PreserveSig]int GetStreamLatency(out long latency);
        [PreserveSig]int GetCurrentPadding(out uint frames);
        [PreserveSig]int IsFormatSupported(int mode,nint format,out nint closest);
        [PreserveSig]int GetMixFormat(out nint format);
        [PreserveSig]int GetDevicePeriod(out long normal,out long minimum);
        [PreserveSig]int Start();
        [PreserveSig]int Stop();
        [PreserveSig]int Reset();
        [PreserveSig]int SetEventHandle(nint handle);
        [PreserveSig]int GetService(ref Guid id,[MarshalAs(UnmanagedType.IUnknown)]out object service);
    }
    [ComImport,Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        [PreserveSig]int GetBuffer(uint frames,out nint buffer);
        [PreserveSig]int ReleaseBuffer(uint frames,uint flags);
    }
    [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]int GetAudioSessionControl(nint id,uint flags,out nint control);
        [PreserveSig]int GetSimpleAudioVolume(nint id,uint flags,out nint volume);
        [PreserveSig]int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        [PreserveSig]int RegisterSessionNotification(IAudioSessionNotification notification);
        [PreserveSig]int UnregisterSessionNotification(IAudioSessionNotification notification);
        [PreserveSig]int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)]string id,nint notification);
        [PreserveSig]int UnregisterDuckNotification(nint notification);
    }
    [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]int GetCount(out int count);
        [PreserveSig]int GetSession(int index,out IAudioSessionControl control);
    }
    [ComImport,Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]int GetState(out int state);
    }
    [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig]int GetState(out int state);
        [PreserveSig]int GetDisplayName(out nint name);
        [PreserveSig]int SetDisplayName(nint name,nint context);
        [PreserveSig]int GetIconPath(out nint path);
        [PreserveSig]int SetIconPath(nint path,nint context);
        [PreserveSig]int GetGroupingParam(out Guid id);
        [PreserveSig]int SetGroupingParam(ref Guid id,nint context);
        [PreserveSig]int RegisterAudioSessionNotification(nint notification);
        [PreserveSig]int UnregisterAudioSessionNotification(nint notification);
        [PreserveSig]int GetSessionIdentifier(out nint id);
        [PreserveSig]int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetProcessId(out uint process);
        [PreserveSig]int IsSystemSoundsSession();
        [PreserveSig]int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)]bool optOut);
    }
    [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig]int SetMasterVolume(float level,ref Guid context);
        [PreserveSig]int GetMasterVolume(out float level);
        [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);
        [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
    }
    [Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionNotification
    {
        [PreserveSig]int OnSessionCreated(IAudioSessionControl control);
    }
}

internal static class StartCueTone
{
    internal static float[] Samples()
    {
        const int frames=5760;var samples=new float[frames*2];
        for(int frame=0;frame<frames;frame++)
        {
            double t=frame/48000.0,fade=Math.Min(1,Math.Min(frame/480.0,(frames-1-frame)/960.0));
            float value=(float)(.085*fade*(Math.Sin(2*Math.PI*660*t)+.45*Math.Sin(2*Math.PI*990*t)));
            samples[2*frame]=samples[2*frame+1]=value;
        }
        return samples;
    }
}

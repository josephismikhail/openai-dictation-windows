using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace OpenAIDictation.Windows;

/// <summary>WinMM calls are serialized on one worker. The native callback only queues a returned header.</summary>
internal sealed class PcmRecorder : IDisposable
{
    private const uint DataMessage=0x3C0;
    private readonly WaveCallback callback;
    private readonly ConcurrentQueue<nint> returned=new();
    private readonly AutoResetEvent signal=new(false);
    private readonly TaskCompletionSource started=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<(nint Header,nint Audio)> buffers=[];
    private Thread? worker;
    private nint handle;
    private volatile bool stopping;
    private Exception? failure;
    private int disposed;
    public event Action<byte[]>? Data;
    public event Action<float>? Level;
    public event Action<string>? Error;
    public PcmRecorder(){callback=OnWave;}
    public void Start()
    {
        if(worker!=null)throw new InvalidOperationException("Microphone capture has already started.");
        worker=new Thread(Run){IsBackground=true,Name="OpenAI dictation audio"};worker.Start();started.Task.GetAwaiter().GetResult();
    }
    private void OnWave(nint input,uint message,nuint instance,nint p1,nint p2)
    {
        if(message!=DataMessage)return;
        // No wave functions, application callbacks, or audio copies inside waveInProc.
        returned.Enqueue(p1);signal.Set();
    }
    private void Run()
    {
        try
        {
            var format=new WaveFormat{Tag=1,Channels=1,Rate=24000,BytesPerSecond=48000,BlockAlign=2,Bits=16};
            Check(waveInOpen(out handle,uint.MaxValue,ref format,callback,0,0x30000));
            for(int i=0;i<6;i++)
            {
                nint audio=Marshal.AllocHGlobal(1920),header=Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
                Marshal.StructureToPtr(new WaveHeader{Data=audio,Length=1920},header,false);buffers.Add((header,audio));
                Check(waveInPrepareHeader(handle,header,(uint)Marshal.SizeOf<WaveHeader>()));
                Check(waveInAddBuffer(handle,header,(uint)Marshal.SizeOf<WaveHeader>()));
            }
            Check(waveInStart(handle));started.TrySetResult();
            while(!stopping)
            {
                signal.WaitOne(250);
                Drain(requeue:!stopping);
            }
        }
        catch(Exception)
        {
            failure=new InvalidOperationException("Microphone capture failed. Check Windows microphone permissions and input device.");
            started.TrySetException(failure);try{Error?.Invoke(failure.Message);}catch{}
        }
        finally
        {
            stopping=true;
            if(handle!=0)
            {
                waveInStop(handle);waveInReset(handle);
                DrainSafely();
                foreach(var buffer in buffers)
                {
                    uint result=waveInUnprepareHeader(handle,buffer.Header,(uint)Marshal.SizeOf<WaveHeader>());
                    // Reset returns every queued buffer. Be conservative if a driver
                    // delays that return: never free a header still owned by WinMM.
                    for(int retry=0;result==33 && retry<200;retry++)
                    {
                        Thread.Sleep(10);DrainSafely();result=waveInUnprepareHeader(handle,buffer.Header,(uint)Marshal.SizeOf<WaveHeader>());
                    }
                    if(result==0){Marshal.FreeHGlobal(buffer.Header);Marshal.FreeHGlobal(buffer.Audio);}
                    else failure??=new InvalidOperationException("The microphone driver did not release its audio buffers.");
                }
                buffers.Clear();waveInClose(handle);handle=0;
            }
            started.TrySetException(failure??new InvalidOperationException("Microphone capture stopped before initialization."));
        }
    }
    private void Drain(bool requeue)
    {
        while(returned.TryDequeue(out nint pointer))
        {
            var header=Marshal.PtrToStructure<WaveHeader>(pointer);
            if(header.Recorded>0)
            {
                byte[] data=new byte[header.Recorded];Marshal.Copy(header.Data,data,0,data.Length);
                double squares=0;for(int i=0;i+1<data.Length;i+=2){double sample=(short)(data[i]|data[i+1]<<8)/32768.0;squares+=sample*sample;}
                Level?.Invoke((float)Math.Min(1,Math.Sqrt(squares/Math.Max(1,data.Length/2))*6));Data?.Invoke(data);
            }
            if(requeue && !stopping)Check(waveInAddBuffer(handle,pointer,(uint)Marshal.SizeOf<WaveHeader>()));
        }
    }
    private void DrainSafely(){try{Drain(false);}catch{failure??=new InvalidOperationException("The final microphone audio could not be read.");}}
    public void Stop()
    {
        stopping=true;signal.Set();
        if(worker!=null && Thread.CurrentThread!=worker && !worker.Join(10000))
            throw new InvalidOperationException("The microphone did not finish stopping. Restart the app before recording again.");
        if(failure!=null)throw failure;
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        Stop();
        // Keep the event and delegate alive for conservative driver-failure handling.
        GC.KeepAlive(callback);
    }
    private static void Check(uint result){if(result!=0)throw new InvalidOperationException("Microphone capture is unavailable.");}
    [StructLayout(LayoutKind.Sequential,Pack=2)] private struct WaveFormat {public ushort Tag,Channels;public uint Rate,BytesPerSecond;public ushort BlockAlign,Bits,Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader {public nint Data;public uint Length,Recorded;public nuint User;public uint Flags,Loops;public nint Next;public nuint Reserved;}
    private delegate void WaveCallback(nint input,uint message,nuint instance,nint p1,nint p2);
    [DllImport("winmm.dll")] private static extern uint waveInOpen(out nint handle,uint device,ref WaveFormat format,WaveCallback callback,nuint instance,uint flags);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(nint handle,nint header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(nint handle,nint header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(nint handle,nint header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveInStart(nint handle);
    [DllImport("winmm.dll")] private static extern uint waveInStop(nint handle);
    [DllImport("winmm.dll")] private static extern uint waveInReset(nint handle);
    [DllImport("winmm.dll")] private static extern uint waveInClose(nint handle);
}

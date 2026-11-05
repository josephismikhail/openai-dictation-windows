namespace OpenAIDictation.Windows;

/// <summary>Best-effort local confirmation, started only after the microphone has opened.</summary>
internal sealed class RecordingStartCue : IDisposable
{
    private readonly CancellationTokenSource cancel;
    private readonly Task worker;
    private IDisposable? deferredGuard;
    private int disposed;
    internal Task Completion=>worker;
    internal string Outcome{get;private set;}="pending";
    internal int FailureCode{get;private set;}

    internal RecordingStartCue(SystemAudioMute mute,CancellationToken token)
        :this(mute,new CoreAudioCuePlatform(),token){}
    internal RecordingStartCue(SystemAudioMute mute,IRecordingCuePlatform platform,CancellationToken token)
    {
        cancel=CancellationTokenSource.CreateLinkedTokenSource(token);
        // Core Audio session notifications require an MTA, not the WPF dispatcher.
        worker=Task.Run(()=>Run(mute,platform));
    }
    private void Run(SystemAudioMute mute,IRecordingCuePlatform platform)
    {
        IDisposable? permit=null,guard=null;
        try
        {
            cancel.Token.ThrowIfCancellationRequested();
            using var output=platform.OpenDefaultOutput();
            // All endpoints are still muted while rendering and session protection are prepared.
            guard=output.ProtectOtherSessions(()=>
            {
                cancel.Cancel();
                try{mute.CloseCue();}catch{}
                mute.ReportCueFailure();
            });
            cancel.Token.ThrowIfCancellationRequested();
            permit=mute.PermitCue(output.Id);
            if(permit!=null){output.Play(cancel.Token);Outcome="played";}
            else Outcome="muted-or-unavailable";
        }
        catch(OperationCanceledException){Outcome="cancelled";}
        catch(Exception error){Outcome="unavailable";FailureCode=error.HResult; /* Feedback is optional; dictation remains usable without a sound. */ }
        finally
        {
            try{mute.CloseCue();}
            catch
            {
                // If a driver refuses re-muting, keep the other sessions silent until
                // the microphone has closed. Never restore playback behind an open mic.
                deferredGuard=guard;guard=null;mute.ReportCueFailure();
            }
            try{guard?.Dispose();}catch{}
        }
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        cancel.Cancel();worker.GetAwaiter().GetResult();
        deferredGuard?.Dispose();deferredGuard=null;cancel.Dispose();
    }
}

internal interface IRecordingCuePlatform
{
    IRecordingCueOutput OpenDefaultOutput();
}
internal interface IRecordingCueOutput : IDisposable
{
    string Id {get;}
    IDisposable ProtectOtherSessions(Action failed);
    void Play(CancellationToken token);
}

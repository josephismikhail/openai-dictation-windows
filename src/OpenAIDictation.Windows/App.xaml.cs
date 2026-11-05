using System.IO;
using System.IO.Pipes;
using System.Threading.Channels;
using System.Windows;
using OpenAIDictation.Core;

namespace OpenAIDictation.Windows;

public partial class App:System.Windows.Application
{
    private readonly CancellationTokenSource lifetime=new();
    private readonly OpenAiClient client=new();
    private Mutex? singleInstance;
    private GlobalShortcut? shortcut;
    private System.Windows.Forms.NotifyIcon? tray;
    private OverlayWindow? overlay;
    private SettingsWindow? settings;
    private Session? current;
    private string recoveryText="";
    private int errorGeneration;
    private string PipeName=>"OpenAIDictation."+Environment.UserName;
    private sealed class Session
    {
        internal required nint Target;
        internal required FocusSnapshot Focus;
        internal required string Key;
        internal required DictationOptions Options;
        internal required RealtimeSession Live;
        internal readonly CancellationTokenSource Cancel=new();
        internal readonly Channel<byte[]> Audio=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(150){FullMode=BoundedChannelFullMode.Wait,SingleReader=true,SingleWriter=false});
        internal readonly MemoryStream Recorded=new();
        internal PcmRecorder? Recorder;
        internal SystemAudioMute? AudioMute;
        internal RecordingStartCue? StartCue;
        internal readonly object CaptureGate=new();
        internal Task Connection=Task.CompletedTask,Sender=Task.CompletedTask;
        internal bool Processing;
        internal DictationStage Stage=DictationStage.Initialization;
        internal volatile bool LiveFailed;
        internal int Disposed;
        internal int LimitSignaled;
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Contains("--import-key-stdin")){try{LocalStore.SaveKey(await Console.In.ReadToEndAsync());Shutdown(0);}catch{Shutdown(1);}return;}
        if(e.Args.Contains("--verify-key")){try{_ = LocalStore.LoadKey();Shutdown(0);}catch{Shutdown(1);}return;}
        singleInstance=new Mutex(true,"Local\\"+PipeName,out bool owner);
        if(!owner)
        {
            try{await using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.Out,PipeOptions.Asynchronous);await pipe.ConnectAsync(2000);await using var writer=new StreamWriter(pipe){AutoFlush=true};await writer.WriteLineAsync(e.Args.FirstOrDefault()??"--settings");}catch{}
            Shutdown();return;
        }
        if(e.Args.Contains("--quit")){Shutdown();return;}
        overlay=new OverlayWindow();overlay.StopRequested+=()=>_ = StopAsync();overlay.CancelRequested+=Cancel;
        tray=new System.Windows.Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Application,Text="OpenAI Dictation — Win+H",Visible=true};
        var menu=new System.Windows.Forms.ContextMenuStrip();menu.Items.Add("Settings",null,(_,_)=>Dispatcher.Invoke(ShowSettings));menu.Items.Add("Start / stop  Win+H",null,(_,_)=>Dispatcher.Invoke(Toggle));menu.Items.Add("Recover last text",null,(_,_)=>Dispatcher.Invoke(RecoverText));menu.Items.Add("Quit",null,(_,_)=>Dispatcher.Invoke(()=>Shutdown()));tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowSettings);
        try{shortcut=new GlobalShortcut{Active=()=>current!=null};shortcut.Toggle+=()=>Dispatcher.BeginInvoke(Toggle);shortcut.Cancel+=()=>Dispatcher.BeginInvoke(Cancel);}catch{ShowError("Win+H could not be registered. Restart the app and try again.");}
        _=ListenAsync(lifetime.Token);
        if(e.Args.Contains("--preview"))Preview();else if(e.Args.Contains("--toggle"))Toggle();else if(e.Args.Contains("--settings") || !LocalStore.HasKey)ShowSettings();
    }
    private async Task ListenAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe=new NamedPipeServerStream(PipeName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);using var reader=new StreamReader(pipe);string? command=await reader.ReadLineAsync(ct);
                await Dispatcher.InvokeAsync(()=>{switch(command){case "--toggle":Toggle();break;case "--quit":Shutdown();break;case "--preview":Preview();break;default:ShowSettings();break;}});
            }
            catch(OperationCanceledException){break;}catch{await Task.Delay(250,ct);}
        }
    }
    private void ShowSettings()
    {
        if(settings!=null){settings.Activate();return;}
        settings=new SettingsWindow();settings.Closed+=(_,_)=>settings=null;settings.Show();settings.Activate();
    }
    private void Toggle(){if(current==null)Begin();else if(!current.Processing)_=StopAsync();}
    private void RecoverText()
    {
        if(string.IsNullOrEmpty(recoveryText)){ShowError("There is no unsaved text to recover.");return;}
        var text=new System.Windows.Controls.TextBox{Text=recoveryText,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto,Margin=new Thickness(12),IsReadOnly=true};
        new Window{Title="Recover dictation — select and copy",Width=600,Height=350,Content=text,WindowStartupLocation=WindowStartupLocation.CenterScreen}.Show();
    }
    private void Begin()
    {
        Session? started=null;
        try
        {
            string key=LocalStore.LoadKey();var options=LocalStore.LoadOptions();
            var focus=FocusSnapshot.Capture();
            started=new Session{Target=focus.Window,Focus=focus,Key=key,Options=options,Live=client.CreateSession(key,options)};current=started;
            var session=started;
            session.AudioMute=SystemAudioMute.Acquire();
            session.AudioMute.Failed+=()=>Dispatcher.BeginInvoke(()=>
            {
                if(current==session && !session.Processing)Fail(session,"Windows could not keep playback muted. Please try again.");
            });
            session.StartCue=RecordingStartCue.Prepare(session.AudioMute,session.Cancel.Token);
            session.Connection=session.Live.StartAsync(session.Cancel.Token);
            session.Sender=Task.Run(async()=>
            {
                await foreach(var chunk in session.Audio.Reader.ReadAllAsync(session.Cancel.Token))
                {
                    if(session.LiveFailed)continue;
                    try{await session.Live.AppendAsync(chunk,session.Cancel.Token);}
                    catch when(!session.Cancel.IsCancellationRequested){session.LiveFailed=true;}
                }
            },session.Cancel.Token);
            session.Recorder=new PcmRecorder();
            session.Recorder.Error+=message=>Dispatcher.BeginInvoke(()=>Fail(session,message));
            session.Recorder.Data+=data=>
            {
                int accepted=PcmLimit.AcceptedBytes(session.Recorded.Length,data.Length);
                if(accepted>0)
                {
                    if(accepted!=data.Length)Array.Resize(ref data,accepted);
                    session.Recorded.Write(data);
                    if(!session.Audio.Writer.TryWrite(data)){session.Cancel.Cancel();Dispatcher.BeginInvoke(()=>Fail(session,"Audio could not be sent fast enough. Please try again."));}
                }
                if(session.Recorded.Length>=WaveFile.MaxPcmBytes && Interlocked.Exchange(ref session.LimitSignaled,1)==0)
                    Dispatcher.BeginInvoke(()=>{if(current==session && !session.Processing)_=StopAsync();});
            };
            session.Recorder.Level+=level=>Dispatcher.BeginInvoke(()=>{if(current==session)overlay?.SetLevel(level);});
            session.Recorder.Start();session.StartCue.SignalMicrophoneReady();
            session.Stage=DictationStage.Recording;overlay!.Present(OverlayState.Recording,session.Target);
            _=ObserveConnection(session);_=LimitRecording(session);
        }
        catch(Exception ex){if(started!=null)Fail(started,DictationFailure.Describe(ex,started.Stage),ex);else{var message=DictationFailure.Describe(ex,DictationStage.Initialization);DictationFailure.Save(DictationStage.Initialization,message,ex);ShowError(message);ShowSettings();}}
    }
    private async Task ObserveConnection(Session session)
    {
        try{await session.Connection;}catch(OperationCanceledException){}catch{session.LiveFailed=true;}
    }
    private async Task LimitRecording(Session session)
    {
        try{await Task.Delay(TimeSpan.FromMinutes(10),session.Cancel.Token);if(current==session && !session.Processing)await StopAsync();}catch(OperationCanceledException){}
    }
    private async Task StopAsync()
    {
        var session=current;if(session==null || session.Processing)return;session.Processing=true;
        string recovery="";
        try
        {
            session.Stage=DictationStage.Stopping;
            overlay!.Present(OverlayState.Processing,session.Target);
            StopCapture(session);
            session.Audio.Writer.TryComplete();await session.Sender;
            session.Stage=DictationStage.Transcription;
            string raw="";
            try{await session.Connection;if(!session.LiveFailed)raw=await session.Live.CompleteAsync(session.Cancel.Token);}
            catch when(!session.Cancel.IsCancellationRequested){session.LiveFailed=true;}
            if(session.LiveFailed || string.IsNullOrWhiteSpace(raw))raw=await client.TranscribeAsync(session.Recorded.ToArray(),session.Key,session.Options,session.Cancel.Token);
            if(string.IsNullOrWhiteSpace(raw))throw new InvalidOperationException("No speech was detected.");
            recovery=raw;
            session.Stage=DictationStage.Cleanup;
            var cleaned=await client.CleanupAsync(raw,session.Key,session.Options,session.Cancel.Token);
            recovery=cleaned.Text;
            if(current!=session || session.Cancel.IsCancellationRequested)return;
            session.Stage=DictationStage.Insertion;
            await TextInserter.InsertAsync(session.Focus,cleaned.Text,session.Cancel.Token);
            if(current==session){current=null;recoveryText="";overlay.Hide();}
        }
        catch(OperationCanceledException){}catch(Exception ex){if(current==session && recovery.Length>0)recoveryText=recovery;Fail(session,DictationFailure.Describe(ex,session.Stage)+(recovery.Length>0?" Use the tray's Recover last text menu to copy the transcript.":""),ex);}
        finally{await DisposeSession(session);}
    }
    private void Cancel()
    {
        var session=current;current=null;
        try{overlay?.Hide();}catch{ /* Display errors must never prevent audio restoration. */ }
        finally
        {
            if(session!=null)
            {
                session.Cancel.Cancel();
                try{StopCapture(session);}catch{}
                session.Audio.Writer.TryComplete();_ = DisposeSession(session);
            }
        }
    }
    private void Fail(Session session,string message,Exception? error=null){if(current!=session)return;DictationFailure.Save(session.Stage,message,error);Cancel();ShowError(message);}
    private static void StopCapture(Session session)
    {
        lock(session.CaptureGate)
        {
            // Drain/close the microphone before bringing speaker audio back,
            // then restore immediately, before transcription or cleanup awaits.
            try{session.Recorder?.Dispose();}
            finally
            {
                session.Recorder=null;
                try{session.StartCue?.Dispose();}
                finally
                {
                    session.StartCue=null;
                    session.AudioMute?.Dispose();session.AudioMute=null;
                }
            }
        }
    }
    private static async Task DisposeSession(Session session)
    {
        if(Interlocked.Exchange(ref session.Disposed,1)!=0)return;
        session.Cancel.Cancel();try{StopCapture(session);}catch{}session.Audio.Writer.TryComplete();try{await session.Sender;}catch{}
        try{await session.Live.DisposeAsync();}catch{}session.Recorded.Dispose();
    }
    private void ShowError(string message)
    {
        if(lifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted)return;
        try{overlay?.Present(OverlayState.Error,NativeMethods.GetForegroundWindow(),message);}catch{ /* A removed monitor must not interfere with cleanup. */ }
        try{if(tray!=null){tray.BalloonTipTitle="OpenAI Dictation";tray.BalloonTipText=message;tray.ShowBalloonTip(5000);}}catch(ObjectDisposedException){}_=HideErrorLater(++errorGeneration);
    }
    private async Task HideErrorLater(int generation){try{await Task.Delay(20000,lifetime.Token);if(current==null && generation==errorGeneration)overlay?.Hide();}catch(OperationCanceledException){}catch(InvalidOperationException){}}
    private async void Preview()
    {
        try{
            if(current!=null)return;nint target=NativeMethods.GetForegroundWindow();overlay!.Present(OverlayState.Recording,target);
            for(int i=0;i<450 && current==null;i++){overlay.SetLevel((float)(.2+.75*Math.Abs(Math.Sin(i*.2))));await Task.Delay(33);}
            if(current!=null)return;overlay.Present(OverlayState.Processing,target);await Task.Delay(5000);if(current==null)overlay.Hide();
        }catch{ShowError("The preview display became unavailable.");}
    }
    protected override void OnExit(ExitEventArgs e){lifetime.Cancel();Cancel();shortcut?.Dispose();tray?.Dispose();client.Dispose();singleInstance?.Dispose();base.OnExit(e);}
}

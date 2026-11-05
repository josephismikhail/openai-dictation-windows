# Development

## Layout

- `src/OpenAIDictation.Core`: API transport, transcription session protocol, cleanup and pure helpers.
- `src/OpenAIDictation.Windows`: WPF indicator/settings, global hotkey, microphone capture, credential storage and insertion.
- `tests/OpenAIDictation.Tests`: synthetic protocol and behavior tests.
- `tests/OpenAIDictation.Windows.Tests`: Windows clipboard snapshot, audio-boundary and start-cue lifecycle tests; these do not access the real clipboard, microphone or speakers.
- `scripts`: build/install and local lifecycle helpers.

The application uses the Windows SDK interfaces exposed by .NET and direct OpenAI HTTPS/WebSocket APIs. It does not depend on a browser runtime or a local model server.

## Checks

```powershell
dotnet restore
dotnet test -c Release
dotnet build src/OpenAIDictation.Windows -c Release
```

Tests must cover failure and race cases: stop before connection readiness, ordered audio delivery, mismatched transcription events, malformed/server errors, cancellation, cleanup response parsing, and clipboard/target guards where practical. No real key belongs in the test suite.

Before shipping, check Win+H start/stop in Notepad, silence, Escape cancellation, settings persistence, microphone release, foreground-window changes, multiple monitors, DPI scaling, and ordinary typing while idle. Do not send any test messages. Test API calls only with an authorized local credential and synthetic speech/text.

## Recording start sound

`RecordingStartCue.Prepare` opens and silently starts its renderer after the output mute is acquired, overlapping microphone setup. `SignalMicrophoneReady` permits playback only after `PcmRecorder.Start()` succeeds. `PresentationReady` completes after the prepared renderer queues the first sound buffer; the dispatcher then shows the recording indicator. Unavailable audio completes the same signal, so muted or failed sound cannot hide the indicator. The dispatcher rechecks session identity, cancellation, and processing state to prevent a late recording overlay after stop or cancel. It never blocks waiting for the audio worker. The cue is Handy's bundled default marimba start sound (about 580 ms), with its original gain and an MIT attribution/license in `Assets`. The 44.1 kHz PCM asset is decoded and resampled to 48 kHz locally. Nothing is downloaded. Preparation runs on an MTA worker so Core Audio can deliver new-session notifications, without blocking the shortcut dispatcher.

The normal recording lease still mutes every active render endpoint. During the chime only, `CoreAudioCuePlatform` uses the explicitly selected default output in WASAPI shared mode, mutes other processes' sessions on that endpoint, and subscribes before enumerating sessions. Only after preparation and microphone readiness may `SystemAudioMute` temporarily release its own mute on that one endpoint. Outputs that were already muted are never released. A device change cannot reroute the sound because the renderer retains the selected endpoint. The complete short asset is queued at once: some drivers block inside `IAudioClient.Start`, making small-buffer refilling unsuitable for this sound.

Cleanup re-mutes the endpoint before restoring session mute states. Stop/cancel drains the recorder, cancels and joins the cue, then restores endpoint states. If a driver refuses the re-mute, session protection stays held until capture is closed and the recording is failed safely. Ordinary cue setup/playback failures skip feedback without disabling dictation. Neither session nor endpoint volume levels are changed.

Device discovery still polls every 250 ms, and Windows delivers session-created notifications asynchronously. Existing playback is protected before the cue opens, but these APIs cannot promise zero transient sound from a newly appearing device/session during discovery.

Synthetic tests cover the microphone-ready gate, cancellation during preparation/playback, initially muted outputs, protection/playback failure, notification failure, remute failure, restoration ordering, and the asset's hash, level, and duration. Actual speaker checks are separate, explicit manual probes; the default test suite never changes system audio. The interfaces follow Microsoft's [session-notification contract](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-registersessionnotification) and [WASAPI stream model](https://learn.microsoft.com/en-us/windows/win32/coreaudio/stream-management).

## Workflow

Use a focused feature branch, make logical commits, and open a PR with behavior and verification results. Run `codex review --base main` where Codex CLI is available, fix actionable findings, and wait for CI before merging. Keep unrelated changes out of the PR.

Release builds should be produced from reviewed source. Keep local artifacts ignored. A framework-dependent publish is smaller but needs the .NET Desktop Runtime; a self-contained publish is larger and carries its runtime. Do not claim a binary is signed unless a signing step actually ran.

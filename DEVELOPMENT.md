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

`RecordingStartCue` starts only after `PcmRecorder.Start()` succeeds. It plays a generated 120 ms stereo tone with short fades, never a downloaded sound file. It runs on an MTA worker so Core Audio can deliver new-session notifications, and does not delay the keyboard shortcut dispatcher.

The tone uses a 0.255 gain for its two sine components, three times the initial 0.085 gain (about +9.5 dB). Synthetic checks constrain both peak and RMS amplitude and verify stereo channels and silent edges. This does not change Windows volume levels, device selection, or mute ownership.

The normal recording lease still mutes every active render endpoint. During the chime only, `CoreAudioCuePlatform` opens the current default output explicitly using WASAPI shared mode, mutes other processes' sessions on that endpoint, and subscribes before enumerating sessions. Only then may `SystemAudioMute` temporarily release its own mute on that one endpoint. Outputs that were already muted are never released. A device change cannot reroute the tone because the renderer retains the selected endpoint.

Cleanup re-mutes the endpoint before restoring session mute states. Stop/cancel drains the recorder, cancels and joins the cue, then restores endpoint states. If a driver refuses the re-mute, session protection stays held until capture is closed and the recording is failed safely. Ordinary cue setup/playback failures skip feedback without disabling dictation. Neither session nor endpoint volume levels are changed.

Device discovery still polls every 250 ms, and Windows delivers session-created notifications asynchronously. Existing playback is protected before the cue opens, but these APIs cannot promise zero transient sound from a newly appearing device/session during discovery.

Synthetic tests cover cancellation during preparation/playback, initially muted outputs, protection/playback failure, notification failure, remute failure, restoration ordering, and tone bounds. Actual speaker checks are separate, explicit manual probes; the default test suite never changes system audio. The interfaces follow Microsoft's [session-notification contract](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-registersessionnotification) and [WASAPI stream model](https://learn.microsoft.com/en-us/windows/win32/coreaudio/stream-management).

## Workflow

Use a focused feature branch, make logical commits, and open a PR with behavior and verification results. Run `codex review --base main` where Codex CLI is available, fix actionable findings, and wait for CI before merging. Keep unrelated changes out of the PR.

Release builds should be produced from reviewed source. Keep local artifacts ignored. A framework-dependent publish is smaller but needs the .NET Desktop Runtime; a self-contained publish is larger and carries its runtime. Do not claim a binary is signed unless a signing step actually ran.

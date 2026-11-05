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

## Local Pixel-style experiment

This branch is a local trial. Do not push, open a PR, or merge it without a later request to publish the experiment. Main retains the shipped pill-and-sound design.

`OverlayWindow` is a non-activating, topmost transparent monitor overlay. Its bounds cover the whole selected monitor, including the taskbar, using physical monitor coordinates; WPF renders in device-independent units. The original editor stays the insertion target. Tests should cover negative monitor coordinates, mixed DPI, centering, processing continuity, and Escape/stop cancellation.

`PixelAudioLevel` ports the Android PCM16 RMS-to-decibel mapping without changing captured audio. The animation ports the Pixel's 49-bar history, attack/release, 450 ms contraction, and rounded breathing processing shape. It uses an 80% black scrim and white foreground. No start-sound object is created on this branch; existing cue code and tests are retained only as dormant baseline functionality.

The normal audio-mute lease remains: playback mutes while the microphone captures and restores when capture stops. Credentials, transcription, cleanup, and safe clipboard restoration are unchanged. The synthetic preview uses no microphone or API and can be canceled with Escape without reappearing later.

To restore the shipped design, switch to `main`, republish, and run the installer. A private backup of the pre-experiment installed binaries is also kept outside the repository; configuration and credentials remain in their usual local storage.

## Workflow

Use a focused feature branch, make logical commits, and open a PR with behavior and verification results. Run `codex review --base main` where Codex CLI is available, fix actionable findings, and wait for CI before merging. Keep unrelated changes out of the PR.

Release builds should be produced from reviewed source. Keep local artifacts ignored. A framework-dependent publish is smaller but needs the .NET Desktop Runtime; a self-contained publish is larger and carries its runtime. Do not claim a binary is signed unless a signing step actually ran.

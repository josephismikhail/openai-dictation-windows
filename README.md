# Windows OpenAI Dictation

**Local experiment: `experiment/pixel-fullscreen-dictation`.** This branch tries the Pixel-style full-screen interface. It has not been pushed or merged into the GitHub project.

A small native Windows app for **Win+H dictation with OpenAI transcription and cleanup**. It dims the active monitor with the Pixel-style voice animation while you speak, then pastes the cleaned text into the app you started from.

This is an independent project, inspired by Handy's minimal recording interface. It is not affiliated with Handy, Microsoft, or OpenAI.

## What it does

1. Place the cursor in a text field and press **Win+H**.
2. Once the microphone is listening, that monitor dims behind a centered white waveform. Speak naturally; pauses do not stop recording. There is no start sound.
3. Press **Win+H** again to finish. The waveform contracts into the same breathing rounded shape used on the Pixel.
4. The app cleans up the transcript and pastes the result. The full-screen view disappears.

Click anywhere on the recording view to finish, or press **Escape** to cancel recording or processing. There is no visible cancel button. The tray menu opens settings or exits the app. The microphone opens only during a recording; there is no always-listening mode.

Recordings stop automatically at ten minutes. If cleanup or insertion fails after a transcript is available, choose **Recover last text** in the tray menu to copy it. Recovery is held only in memory and disappears when the app exits or a later dictation succeeds.

Cleanup removes filler words, applies explicit spoken corrections, and formats punctuation and lists. Add preferred names and spellings in settings. Like any speech model, it can make mistakes: review text before sending it. Dictation does not press Send or submit forms.

Configured vocabulary preserves its exact capitalization after cleanup. Personal spelling preferences are stored in local settings and are not built into the public source.

## Requirements

- Windows 10 or Windows 11, x64.
- .NET 9 Desktop Runtime to run the framework-dependent build; .NET 9 SDK to build it.
- A microphone, internet access, and your own OpenAI API key with available API credit.

ChatGPT subscription usage and API billing are separate. A dedicated API key makes it easier to identify this app's usage in OpenAI's usage dashboard.

## Build and install

```powershell
dotnet restore
dotnet test
dotnet publish src/OpenAIDictation.Windows -c Release -r win-x64 --self-contained false -o artifacts/publish
./scripts/install.ps1
```

The installer places the app under `%LOCALAPPDATA%\OpenAIDictation\app` and adds a per-user startup shortcut. It does not require administrator access.

If your existing Handy helper already captures Win+H, use `./scripts/install.ps1 -ReplaceHandy`. This stops that specific helper and backs up its startup shortcut. It leaves the Handy installation available for rollback. Avoid running two apps that intercept the same shortcut.

Open **Settings** from the tray menu, enter your own API key, and save it. Keys are encrypted using Windows DPAPI for the current account at `%LOCALAPPDATA%\OpenAIDictation\credential.dpapi`. The key is never included in source code or a release binary.

## Interface

The active monitor is covered by an 80% black translucent layer, leaving its contents visible without blur. Other monitors are unchanged. The centered white waveform uses the Pixel's 49 bars, microphone-level mapping, history, and smoothing. When recording stops, it contracts over 450 ms into the Pixel's gently breathing rounded processing shape. The view stays until insertion finishes or the session is canceled.

The overlay does not activate or take keyboard focus from the original editor. The existing insertion-target checks still apply. Escape cancels; the same Win+H shortcut starts and stops recording. Reduced system animations use a static processing shape. There are no routine status labels, start sounds, or visible cancel controls.

Errors show a complete safe explanation. Escape or a click dismisses the error view; it also closes automatically after 20 seconds unless replaced by a new recording.

## Audio while recording

Starting dictation temporarily mutes every active Windows audio output, including playback from browsers, Spotify, and other apps. It preserves devices that were already muted and does not change volume levels. Audio returns after microphone capture stops, before transcription and cleanup finish. Canceling or normally exiting also restores the previous state. Newly active outputs are checked while recording.

This experiment does not create a start-sound renderer or play any recording cue.

This changes Windows' output mute state. If the process is forcibly terminated or crashes while recording, Windows may remain muted; unmute it using the system volume control.

## OpenAI processing and privacy

The phone or laptop does not run these models locally. This app connects directly to OpenAI, without a project-operated proxy or account service:

- Live transcription: `gpt-live-transcribe`, PCM audio at 24 kHz, mono, 16-bit.
- File transcription fallback: `gpt-transcribe`.
- Cleanup: `gpt-6.1-sol`, low reasoning effort, requesting the `ultrafast` service tier.
- Responses request `store: false`; this does not override OpenAI's applicable retention or billing terms.

Audio, transcripts, vocabulary, and cleanup instructions are sent to OpenAI when you use dictation. They are not sent to the repository maintainer. Audio is buffered in memory; the application does not maintain a transcript history or telemetry service. The app temporarily uses the Windows clipboard to paste, then restores it when safe. Applications, clipboard managers, and Windows clipboard history may independently observe clipboard contents.

For troubleshooting, the latest safe error explanation, processing stage, exception types, and numeric error codes are saved locally to `%LOCALAPPDATA%\OpenAIDictation\last-error.json`. This single report is overwritten by the next error and contains no recording, transcript, clipboard content, API key, provider response body, or stack trace. It is never uploaded.

The microphone permission, networking, global shortcut, and clipboard behavior are explained in [SECURITY.md](SECURITY.md). Implementation details and tests are in [DEVELOPMENT.md](DEVELOPMENT.md).

## Limits

- This app replaces Windows' normal Win+H behavior while it is running. Exit it to release the shortcut.
- Text insertion depends on the destination accepting a standard paste. Elevated apps, games, protected fields, remote desktops, and custom editors may restrict automation.
- It verifies the destination before pasting; if you switch apps, it should refuse to paste into a different window.
- Clipboard contents are copied before pasting. If a format cannot be safely preserved, the app leaves the clipboard alone and makes the result available through **Recover last text**.
- The cleanup model can change meaning or miss names despite instructions. Vocabulary hints improve recognition but cannot guarantee perfect spelling.
- Availability, latency, and charges depend on the API models and your account. Ultrafast is a requested service tier, not an application-level speed guarantee.

## Development and licensing

Work on a feature branch, add meaningful tests, and open a pull request. Never commit API keys, private recordings, local configuration, build output, or signing material. Public CI uses synthetic data and no real API credentials.

Licensed under GPL-3.0-only. The interface takes inspiration from the open-source [Handy project](https://github.com/cjpais/Handy); this app is independently implemented in C# and WPF. API protocol behavior also follows our [Android dictation project](https://github.com/josephismikhail/gboard-openai-dictation).

Official API references: [Realtime transcription](https://developers.openai.com/api/docs/guides/realtime-transcription), [Ultrafast mode](https://developers.openai.com/api/docs/guides/ultrafast-mode).

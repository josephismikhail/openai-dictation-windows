# Development

## Layout

- `src/OpenAIDictation.Core`: API transport, transcription session protocol, cleanup and pure helpers.
- `src/OpenAIDictation.Windows`: WPF indicator/settings, global hotkey, microphone capture, credential storage and insertion.
- `tests/OpenAIDictation.Tests`: synthetic protocol and behavior tests.
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

## Workflow

Use a focused feature branch, make logical commits, and open a PR with behavior and verification results. Run `codex review --base main` where Codex CLI is available, fix actionable findings, and wait for CI before merging. Keep unrelated changes out of the PR.

Release builds should be produced from reviewed source. Keep local artifacts ignored. A framework-dependent publish is smaller but needs the .NET Desktop Runtime; a self-contained publish is larger and carries its runtime. Do not claim a binary is signed unless a signing step actually ran.

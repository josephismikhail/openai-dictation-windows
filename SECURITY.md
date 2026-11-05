# Security and privacy

## Credentials

API keys are stored as a DPAPI blob scoped to the current Windows user, outside the source repository. Other Windows accounts cannot normally decrypt that blob. Software running as your account or with administrative access may still access it; encryption is not protection from a compromised account.

The settings field must remain masked and must not refill with the saved key. Blank input preserves an existing credential. Never put a key in a command-line argument, log, screenshot, issue, or test fixture. The optional key-import command accepts standard input instead of command-line text.

## Audio and network

The microphone is opened only for an explicit recording and is closed on stop, cancel, error, or shutdown. A bounded recording length protects against forgotten sessions. Audio is kept in memory for streaming and fallback, then released. Public tests do not open a real microphone or use an API key.

Recording temporarily mutes active output devices through Windows Core Audio. The app remembers which devices it muted and restores them after capture ends, including cancellation and normal shutdown. It never changes the microphone volume or output volume levels. Forced termination cannot run cleanup, so manual unmuting may be needed after a crash.

Production requests go directly to `api.openai.com` over TLS. No project server, telemetry collector, analytics SDK, or automatic update service is present. Response bodies and transcripts must not appear in error logs. Error messages should describe the failure without including credentials or private dictation.

OpenAI receives the audio and cleanup inputs. Its account terms and retention policies apply. `store: false` is a request option, not a claim of zero retention.

## Keyboard and clipboard

The app registers a global keyboard hook for Win+H, and Escape while a session is active. It must not log ordinary keystrokes. Insertion requires the original destination window to remain valid and active. The app never synthesizes an Enter key or sends messages.

Paste temporarily puts cleaned text on the clipboard. The previous clipboard is restored if the app still owns the temporary value; a new clipboard value must not be overwritten. Clipboard managers and Windows history operate independently of this application.

## Reporting

Report vulnerabilities without including a working key, private audio, or private text. If a key is accidentally disclosed, revoke it in OpenAI Platform immediately. Review every diff before publishing, including fixtures, screenshots, and generated files.

# Security

## Reporting a vulnerability

Please do not open a public issue. Use GitHub's "Report a vulnerability" (private security advisory) on this repository, and include the version, steps to reproduce and the impact. You will get an answer as soon as possible.

## Threat model

- **Local API**: listens only on `127.0.0.1`, every request needs the bearer token (stored with DPAPI-protected settings), and the `Origin` and `Host` headers are checked so a web page cannot call it. Anything running as your Windows user can read the token file; that is by design.
- **AI assistants (MCP)**: the MCP server marks its requests as an assistant's and the app refuses what an assistant may not do (arbitrary service calls, URL calls and multi-actions, scripts and shells as targets, closing or shutting down on a plain press). "More power" is an environment variable only the user sets. A process that has the token can still call the plain API without these limits.
- **Secrets** (service tokens, passwords) are encrypted with Windows DPAPI for the current user. They are not safe against malware running as you.
- **Stream Deck plugins** are third-party programs started by MasterHub with **no sandbox**. Install only plugins you trust. A plugin that is checked as unable to run is not started, and a plugin that uses a lot of CPU is flagged (not stopped). Node.js is downloaded only after you agree, from `nodejs.org`, and the zip is checked against the SHA-256 in the release's `SHASUMS256.txt` before `node.exe` is extracted into `%APPDATA%\MasterHub\runtime`.
- **Imported profiles** can contain actions that start programs or scripts. Imports always get a new id and the media is copied under a content-hash name, and keys that can start programs, type text or call addresses are listed in a confirmation before anything is saved; the profile check then marks what this PC lacks. Import only profiles you trust.
- The app does not run elevated and does not open any network port other than the loopback ones above.

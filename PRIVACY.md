# Privacy

MasterHub has no account, no analytics and no crash reporting. Your profiles, usage data and settings live in `%APPDATA%\MasterHub` on your PC and are not uploaded anywhere.

## Outbound connections

The app only contacts a service when you use the feature that needs it.

| Host | Why | When |
| :-- | :-- | :-- |
| `openactionapi.github.io` | Stream Deck plugin index and plugin icons (the public OpenAction plugin registry; nothing about you is sent) | You open the plugin store |
| `api.github.com`, `github.com`, `*.githubusercontent.com` | Plugin release downloads from the repository the index points to | You install a plugin |
| `api.open-meteo.com`, `geocoding-api.open-meteo.com` | Weather live value (the city name you typed is sent) | A key shows the weather |
| `api.spotify.com`, `accounts.spotify.com` | Spotify control | You connected Spotify |
| `discord.com` | Discord control | You connected Discord |
| `api.twitch.tv`, `id.twitch.tv`, `clips.twitch.tv` | Twitch control | You connected Twitch |
| `api.kick.com`, `kick.com` | Kick control | You connected Kick |
| `www.googleapis.com`, `www.youtube.com` | YouTube control | You connected YouTube |
| `homeassistant.local` (or the address you set) | Home Assistant | You set it up |
| OBS, VLC, JetKVM, Voicemod, Streamlabs, Teams | Your own PC or LAN, at the address you set | You connected them |
| `api.github.com` | Update check: the latest release of the repository you entered (nothing about you is sent) | You turned on "Check automatically" or pressed "Check now" |
| `www.google.com` | Opens the default browser | You use the "open browser" action |
| `nodejs.org` | Node.js for a Stream Deck plugin that needs it (index, checksum file and the zip) | You press "Download Node.js…" on a plugin card and agree |
| The address you entered | "Call a URL" key: the method, address and body you typed | You press that key |

Audio analysis, the assistant (app usage, key press counts) and the local API run only on your PC.
The log page shows once a minute how many outbound HTTP calls went to which host (host names only, no paths or tokens); the count stays on your PC.
"Make a language file" and the profile check work only with files on this PC. The local API listens on `127.0.0.1` and needs the token stored in `%APPDATA%\MasterHub`.

## Claude Code status

If you add the `masterhub-cli --claude-hook` hooks (see `docs/CLAUDE_CODE_HOOKS.md`), Claude Code tells the app on this PC only the session id, the project folder name, the event and the tool name of a permission. Prompts, tool inputs, messages and paths are never read from the hook input. Nothing leaves your PC, and the state is kept in memory only.

## Stored secrets

Tokens and passwords are encrypted with Windows DPAPI for your user account.

## Your data

The assistant stores `usage.json` (time in apps) and `presses.json` (key press counts) only while you have switched it on. Turning it off and choosing "Delete data" in Settings removes them.

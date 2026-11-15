# Changelog

All notable changes to MasterHub. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [0.1.0] - 2026-11-15

First public release (pre-release: not every part has been tried with real hardware yet).

### Added
- Local HTTP API (loopback + bearer token) and the `masterhub-cli.exe` command line client (`status`, `profiles`, `profile`, `key`, `lights`, `doctor`).
- Opt-in assistant: profiles prepared for installed apps and games, hints about apps without a profile, new modules and unused keys. Everything stays on this PC.
- Profile drafts (try a change, keep or discard it), quarantine of broken profiles, profile import/export packages.
- Knobs and dial display modules, audio and media actions, timers, smart profile rules (see `docs/ROADMAP.md`).
- Claude Code status on the deck: `masterhub-cli --claude-hook`, `POST /api/agent`, `POST /api/state` / `GET /api/status` (six shared states), "When" rules and `{status}` tokens, orange edge pulse while something waits for you.
- Wallpaper of the whole deck: a GIF or a video works like a picture, it can be zoomed and moved as one piece, and keys with text no longer cut a hole in it (the text is laid over the wallpaper).
- Key properties are split into tabs: Action, Text, Image, Rules, Wallpaper; font and position are folded away under each text.
- Deck brightness keys: brighter, dimmer, cycle levels (never below 1%).
- Update check (off by default; needs a GitHub repository to be entered; only announces, installs nothing).
- MCP design server `masterhub-cli --mcp` with limits for assistants enforced in code (see `docs/MCP.md`).
- Press / Hold / Double: a Hold or Double action no longer draws a bare key on the LCD.
- Icon library: 2 359 Lucide icons (ISC), searchable, in any colour, become the key's picture ("Icons" in the Image tab).
- Power keys show a bar that fills while you hold them; "Clear all keys on this page" (asks first, one Ctrl+Z brings them back).
- Profile check: after an import and with "Check" on the Profiles page, a list of controls that point at something this PC lacks (program, file, picture, sound, audio device, OBS scene, Home Assistant entity, plugin, a name that opens nothing).
- "Make a language file" in Settings (all texts in English for translating) and safer loading of own language files.
- Banners in the window for failed key actions and warnings, e.g. when the program in front runs as administrator and Windows would drop the key presses.
- The autostart entry is renewed for the current program path at every start; the log shows outbound calls per host once a minute.
- Stream Deck plugins: a badge and plain reasons when a plugin cannot run here, a CPU flag for busy plugins, Node.js resolved by the version the plugin needs (download from nodejs.org only after you agree, checksum verified), actions with more than two states, and the uninstall question lists the keys that use the plugin (`docs/STREAMDECK_COMPAT.md`).
- Small actions: type text without the clipboard, seek with the player's own keys, volume and mute of the program in front (key, knob, fader), push-to-talk / push-to-mute, "Everything else" fader, "Call a URL", knob/dial modes, deck brightness on a knob, optional Windows volume popup for faders.

- The title bar says "Connecting…" (amber) for the first seconds while the deck is still being found, instead of a red "Disconnected"; a deck that goes away later is still reported at once.
- Spotify is asked far less often (about 240 requests an hour instead of 1 800): shortly after the track is due to end, every 15 s while playing, every 30 s when paused or idle, never sooner than Spotify's Retry-After. Pressing a key still refreshes at once; a pause made in another app can take up to 15 s to show.
- Tray menu: a list of profiles (the current one ticked), "Start with Windows" and "Open data folder".
- Key text in Japanese, Chinese, Korean and other scripts the chosen font lacks is drawn with a system font that has them instead of empty boxes.
- Importing an official `.mhz` profile: an action without an equivalent stays as a key labelled "Not supported: name" (lost multi-action steps are named too, unmapped sliders, rollers, knobs and the dial go to the log) instead of vanishing.

### Changed
- The Stream Deck plugin store reads its index from the public OpenAction plugin registry (`openactionapi.github.io`), which also gives each plugin an icon; plugins are still downloaded straight from their GitHub repositories. An earlier index file in the cache keeps working when the registry cannot be reached.

### Fixed
- The WebSocket server of the plugin host no longer trusts the Origin `null` (a sandboxed frame of a foreign web page); the local API no longer returns exception texts in its 500 answers (they go to the log).
- Profile rules: a rule rebuild (e.g. after saving a profile) no longer makes the deck stay on a game profile after leaving the game; a window change that arrives while another is being evaluated is no longer lost; the foreground hook no longer waits a second for itself when stopped from its own callback.
- A damaged section of `settings.json` no longer sends the whole file to quarantine (only an unreadable file does); closing a plugin socket now sends its close frame; the deck manager no longer disposes a token its still-running poll thread uses.
- Stream Deck plugins made only for Linux or macOS (e.g. a `run.sh` launcher, `OS: linux`) never ran on Windows and the store still said "installed and started". They are now recognised ("Made only for Linux, not for Windows"), a plugin installed from the store that cannot run is taken out again with the reason, and the message after an install says honestly whether the plugin runs, cannot run, or started but did not answer.
- Key properties tabs: the blue marker of the selected tab no longer touches the label.
- Choosing an action is done in one place, the Action library (search + categories): the Action tab of a key shows what Press / Hold / Double does now with "Change…" (opens the library for that slot, with a banner and a way back to Press) and a clear button; the long flat lists of actions are gone from the properties, which keep only the settings of the action.
- Tray menu looks like the rest of the app (dark card, rounded corners, real check marks / radio dots for the current profile and the switches); right-click menus use the same style.
- A picture, GIF or video on a single LCD key now fills the whole key: no black margin, no rounded corners cut out and no frame on top (also for Stream Deck plugin pictures). A key that is toggled on and has no second picture of its own still shows a thin ring.
- A photo chosen after a library icon (or after zooming another picture) starts at "Fill" with no zoom or offset instead of keeping "Fit" with black bars.
- A key picture replaced under the same file name is drawn again instead of the old cached image; an unreadable key picture is noted on the log page.
- Multi-action keys now run their steps (they did nothing before); pauses are capped at 10 s and do not block the app.
- Starting or stopping a Stream Deck plugin asked "Uninstall plugin?".
- The red "failed" corner and the new hold bar no longer throw on a broken key picture.
- The language loader no longer tries to read the profile templates as translations (an error per template at every start); the preset icons of a key no longer log binding errors.
- Settings: saving a setting on the Settings page no longer resets the assistant's opt-in and answers.
- `ObjectDisposedException` when browsing or installing Elgato Stream Deck plugins (icons were disposed while still on screen).
- Floating hints and dialogs are now opaque and readable over any page.

## 2026-10-23
### Fixed
- Stream Deck plugins: `HttpRequestException` flood, lazy icon loading, full plugin settings and protocol options.
- Binding error on the Action Store page; idle plugin start-up exit.
- Graceful shutdown: no `ObjectDisposedException`, `HttpListenerException` or `WebSocketException`.
- Start-up `WebSocketException`, orphan node processes, redundant licence file.

## 2026-10-22
### Added
- Stream Deck plugin settings window; Stream Deck runtime, Home Assistant, three Knobs and Dial Display modules, unified documentation.

## 2026-10-18
### Added
- Initial project files and MIT licence.

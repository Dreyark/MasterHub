# MasterHub .NET — Product Roadmap & Architecture Opportunities

> **Target Framework**: .NET 10.0 (C# 13, Windows x64, Avalonia 12, SkiaSharp)  
> **Build Command**: `dotnet build MasterHub.slnx --nologo` (0 Warnings, 0 Errors)  
> **Test Command**: `dotnet test MasterHub.slnx --nologo` (1119 core/integration/integrity tests + 331 headless UI tests passing)  
> **Last audit**: 2026-11-12 (see [§4](#4-code-audit-2026-11-12--results))  
> **Feature backlog**: [§3.1](#31-feature-backlog) — 37 tasks (`T-01`…`T-37`) with priority, size and status. The OPP-xx items below map to tasks in [§3](#status-of-the-opportunities-audit-2026-11-12); open defects are in [§5.1](#51-defects--risks) (B-14 … B-20) and performance ideas in [§5.1a](#51a-performance-backlog-measure-first) (P-07 … P-09).

**Status legend**: ✅ implemented & verified in code · 🧪 implemented, covered by unit tests · ⚠️ implemented, needs work (see backlog) · ⏳ planned (not started).
Items marked ✅/🧪 were verified **statically** (code review + build + unit tests). Anything that needs physical hardware or a running GUI
(USB HID, LCD rendering, third-party services) was **not** exercised in the audit and is marked "needs hardware smoke test" in §5.

---

## 1. Architecture at a glance

| Dimension | MasterHub .NET Studio |
| :--- | :--- |
| **Core tech stack** | **C# (.NET 10 + Avalonia 12 native XAML)** |
| **Memory footprint** | **~32–38 MB RAM, < 1 % CPU idle** (not re-measured in the audit) |
| **Rendering** | Native vector engine + SkiaSharp 72×72 cache; the Studio shows the very picture sent to the deck |
| **USB HID layer** | **Pure Win32 HID (`SetupAPI`, zero vendor DLLs)**; one write at a time (P-07) |
| **Hardware safety** | `IsForbiddenReport` guard (20 forbidden commands); T-35 adds a test that no report builder can emit one |
| **Credential security** | **Windows DPAPI (`enc:v1:`)** |
| **Stream Deck runtime** | Bi-directional WebSocket 28123 + Node.js + headless Chromium/Edge |
| **Hardware telemetry** | Direct DXGI, NVML, AMD ADL2, PDH, RTSS FPS (no external tool) |
| **Peripheral drivers** | Razer + Wooting HID; no MIDI/vJoy yet (OPP-06) |
| **Integrations** | Broad (OBS, Streamlabs, Spotify, Home Assistant, Twitch, Kick, YouTube, Discord, Teams, VLC, Voicemod, JetKVM…); depth of Streamlabs/OBS actions still to grow (T-34 #21) |
| **Crash safety** | `HardwareWatchdog` extinguishes the hardware; no automatic restart or crash report yet (B-15) |
| **Automation surface** | Local HTTP API (T-04), CLI `masterhub-cli` (T-24), status board and Claude Code hook (T-25), MCP design server `masterhub-cli --mcp` (T-26) — partly done |
| **Profiles UX** | Backups, `.mhdeck` sharing, tour, drafts, undo, drag/swap, press/hold/double on one key, conditional looks (T-01…T-12, mostly done) |
| **Visuals** | Static/animated panoramic wallpaper, icon library (T-21 a); living deck, screensaver and lights-to-music are planned (T-20…T-23) |

---

## 2. Completed Milestones

### 2.1. Hardware Modules & Zero-Latency Engine
- [x] **Core Modules**: Base Station (`0x01F6`), Key Deck 15 (`0x01FE`), Fader 5 (`0x0200`), Roller 2 (`0x0204`).
- [x] **3 Knobs Module (`0x0202`)**: 3 rotary encoders with push buttons, RGB LED rings with 10 hardware lighting modes (`0x02 0x2C`), speed control (`0x02 0x30`), and velocity acceleration.
- [x] **Circular Dial Module (`0x0206`)**: 240×240 IPS circular display (`0x02 0x45`), outer dial ring, software mode activation (`0x03 0x49`), and brightness control (`0x03 0x44`).
- [x] **Dock Accelerometer / Orientation Sensor**: Automatic canvas re-orientation via Feature Report `0x35` (1 = Horizontal, 0 = Vertical).
- [x] 🧪 **Hardware Safety Guard**: `MasterHubDeviceHandle.IsForbiddenReport` prevents firmware corruption, factory calibration wipe (`0x02 0x2F`), and SPI flash degradation.
- [x] 🧪 **Analog Deadband & Smoothing**: Fader jitter filtering (`<= 2` -> 0%, `>= 98` -> 100%, and < 2% noise delta suppression).
- [x] **Dynamic Encoder Acceleration**: Velocity-based multipliers (1×, 2× for <110ms, 3× for <50ms) for rollers, knobs, and dial.
- [x] **Auto-Standby Power Management**: Hardware sleep and wake-up on Windows lock/unlock (`SystemEvents.SessionSwitch`).
- [x] **Physical Roller Mapping**: Right roller mapped to offset, left roller mapped to offset+1.

### 2.2. LCD Graphics & Visual Pipeline
- [x] **SkiaSharp Pre-Warmed Caching**: Allocation-free 72×72 pixel JPEG rendering pipeline.
- [x] **Full-Panel Panoramic Wallpaper (`0x02 0x35`)**: Single 480×272 frame delivery across all 15 screens in ~11 ms.
- [x] **High-Resolution Icon Extraction**: Native Win32 `PrivateExtractIconsW` / `SHDefExtractIconW` extracting 128×128 and 256×256 pixel icons from `.exe` and `.lnk` files.
- [x] **SVG Rasterizer**: Real-time vector rendering using `Svg.Skia`.
- [x] **Animated Key Displays**: Hardware-accelerated GIF and MP4 video loop playback via `MediaFoundationVideoDecoder`.

### 2.3. Stream Deck Plugin Ecosystem
- [x] 🧪 **Loopback WebSocket Server**: Local WebSocket host on port 28123 conforming to Elgato Stream Deck SDK.
- [x] **Tri-Modal Plugin Host (`StreamDeckPluginHost`)**:
  - *Node.js Plugins*: Executed with `node.exe`.
  - *HTML/DOM/Canvas Plugins*: Executed in background via headless Microsoft Edge / Chrome (`--headless=new --remote-debugging-port=0 --disable-gpu`) with runner invoking `connectElgatoStreamDeckSocket`.
  - *Native Binaries*: Direct `*.exe` execution.
- [x] **Elgato DRM Auto-Reconstruction**: Reconstructs valid `manifest.json` from encrypted or damaged plugins.
- [x] **Dynamic LCD Key Compositor**: Real-time SkiaSharp compositing of `setImage` frames (SVG, Canvas data URI, base64 PNG) with `setTitle` multi-line text, alert indicators (⚠️), checkmarks (✅), and pressed overlays.
- [x] **Plugin Catalog Tab**: Integrated catalog browser with one-click installation, manifest reconstruction, and unzipping.

### 2.4. Smart Home, Streaming & External Integrations
- [x] **Home Assistant**: REST API + live bidirectional WebSocket state updates (`homeassistant.toggle`, `light.turn_on`, brightness via dials).
- [x] **OBS Studio**: obs-websocket v5 client (scenes, streaming, recording, virtual cam, replay buffer, studio mode).
- [x] **Streamlabs Desktop**: JSON-RPC 2.0 via WebSocket port 59650 and Named Pipe `\\.\pipe\slobs` for scene switching.
- [x] **Twitch**: Helix API & chat client (Emote-Only, Slow Mode, Sub-Only, Clear Chat, Send Message, Clips, Markers, Raids, Commercials).
- [x] **Kick**: Pusher WebSocket & Kick API (Chat modes, clear chat, send message, create clip).
- [x] **YouTube Live**: Official YouTube Data API v3 + keyless public scraping fallback for live status and concurrent viewer count.
- [x] **Discord**: Local IPC RPC (Mute, Deafen, Camera, Go Live screen share, Push-To-Talk, Join Voice Channel).
- [x] **Microsoft Teams**: Desktop meeting shortcuts (Mute, Camera, Raise Hand, Leave Meeting) with active window focus restoration.
- [x] **Spotify**: Web API with PKCE authentication (Playback, Shuffle, Repeat, Like Track, Add to Queue, Play URI).
- [x] **Voicemod**: Official Control API 1.0.0 (voice changer, soundboard, mic mute, background FX).
- [x] **JetKVM & VLC**: Remote KVM ATX power switching and VLC HTTP playback control.
- [x] **Windows Media (GSMTC)**: Global System Media Transport Controls extracting title, artist, album, and artwork from Spotify, YouTube, and media players.

### 2.6. Mouse & Keyboard Macros (Razer Synapse-style, software)
- [x] 🧪 **Macro engine** (`MasterHub.Core/Macros`): key/mouse/wheel/text/delay steps; playback modes *Once*, *Repeat while held*, *Toggle*, *Repeat N times*; held keys/buttons are always released when a run is cancelled; loops without delays get a 5 ms minimum gap; Pause/Break stops every running macro.
- [x] 🧪 **Recorder**: global low-level keyboard/mouse hooks, delay modes *Actual / Fixed / None*, ignores key auto-repeat and orphan releases, trims the "Stop" click.
- [x] 🧪 **Triggers**: mouse side buttons X1/X2, middle button, wheel tilt left/right, or any keyboard key (e.g. F13–F24 remapped from DPI-style buttons in Synapse). Left/right mouse buttons can never be triggers. Optional filter "Razer mouse only" (Raw Input device id, VID 0x1532).
- [x] **UI**: *Macros* page (list, editor, steps, triggers) and a *Run macro* deck action with a macro picker. Library stored in `%APPDATA%\MasterHub\macros.json`.
- ⚠️ **Limits (by design)**: macros are *software* macros – they run while MasterHub is running (also in the tray). They are **not** written to the mouse's on-board memory (the button-remap protocol is not public, and the Safety Guard forbids flash writes). Buttons that Synapse handles on the device never reach Windows. The hooks/Raw Input/SendInput layer and the UI were **not exercised on real hardware** in the audit (see §5.3).

### 2.5. Profile Engine, Productivity & Security
- [x] 🧪 **Windows DPAPI Encryption**: Hardware/user-backed encryption (`enc:v1:`) for all sensitive credentials (`SecretProtectionService`).
- [x] **Data Isolation**: All user configuration isolated under `%APPDATA%\MasterHub\`.
- [x] ⚠️ **Cooler Master `.mhz` Importer**: Full migration support for official MasterHub profile archives.
- [x] **Smart Profiles** (2026-11-13, T-01): rules per profile, event-driven foreground detection (`SetWinEventHook` + 250 ms `GetForegroundWindow()` safety net, nothing runs without enabled rules), last-picked return semantics, automatic switches are not persisted, global Auto-switch toggle (Profiles page + tray), "Link app…" picker (running apps + installed apps/games, T-36). Not exercised on real GUI/hardware yet.
- [x] **Multi-Page & Folder Navigation**: Unlimited pages and nested folder hierarchies with automatic return button on key 0 (`GoBack`, `GoHome`).
- [x] **Windows Desktop Controls**: Virtual Desktops (Next, Prev, Create, Close), Task View (`Win+Tab`), Show Desktop (`Win+D`), Clipboard History (`Win+V`).
- [x] 🧪 **Multi-Actions & Conditionals**: Action sequences with delays and conditional logic (IF process running THEN action ELSE action).
- [x] **Multi-Engine Telemetry**: DXGI, NVML (NVIDIA), AMD ADL2 (Radeon/Ryzen), PDH (GPU 3D, Disk), and RTSS Shared Memory (accurate game FPS, 0 FPS on desktop).

---

## 3. Upcoming Roadmap & Execution Blueprints

Below are the remaining technical opportunities identified for future implementation phases:

### [OPP-01] Hardware Monitor Brightness Control via DDC/CI (VCP)
- **Concept**: Control physical external monitor brightness directly through hardware VCP commands via Win32 `dxva2.dll`.
- **Target Controls**: Faders (0..100%) and Rollers/Knobs (±2% step).
- **Key APIs**: `GetPhysicalMonitorsFromHMONITOR`, `GetMonitorBrightness`, `SetMonitorBrightness`, `DestroyPhysicalMonitors`.
- **Value**: Hardware-level backlight adjustment without touching physical monitor buttons or using software color tinting.

### [OPP-02] Built-in Soundboard Engine (NAudio)
- **Concept**: Local audio playback engine for WAV, MP3, OGG, and FLAC sound effects triggered by LCD keys.
- **Playback Modes**: `Play/Stop`, `Restart`, `Overlap`.
- **Output Device**: Output selection targeting default playback device or virtual audio cable (e.g. for Discord/OBS).

### [OPP-03] Dynamic Timer, Pomodoro & Stopwatch Key Tiles
- **Concept**: Real-time animated LCD tiles counting down intervals or tracking elapsed time.
- **Tiles**: Countdown Timer (with flashing/audio notification), Pomodoro (25m focus / 5m break with visual color shifts), Stopwatch (start/pause/split/reset).

### [OPP-04] Full-Deck Screensaver (Matrix Rain, Flip Clock, Starfield)
- **Concept**: After a configurable idle timeout (e.g., 5–15 minutes), render an aesthetic animated screensaver across all 15 LCD screens.
- **Protection**: Prevents IPS image retention. First tap on any key wakes up the deck without triggering the assigned action.

### [OPP-05] Model Context Protocol (MCP) Deck Server (`--mcp-deck`)
- **Concept**: Embedded Model Context Protocol (MCP) server over standard I/O (JSON-RPC), allowing AI agents (such as Antigravity, Claude, Cursor) to interact with the physical desk hardware.
- **Tools**:
  - `notify(status, message)`: Lights up Base Station LED strip and shows alert.
  - `ask_physical(question, options[])`: Prompts user with choices displayed on LCD keys.
  - `read_controls()`: Reads current physical slider and knob positions.

### [OPP-06] MIDI Controller & Virtual Joystick (vJoy) Emulation
- **Concept**: Emulate standard MIDI input/output (WinMM) and virtual game controller axes (vJoy).
- **Use Cases**: DAW volume mixers (Ableton, FL Studio, Reaper) and flight/racing simulator throttles (MS Flight Simulator, Star Citizen).

### [OPP-07] Automatic Profile Snapshots & Versioned Backups
- **Concept**: Automated daily or pre-modification snapshots stored in `%APPDATA%\MasterHub\backups\`, enabling one-click profile recovery.

### Status of the opportunities (audit 2026-11-12)

| ID | Status | Notes from the audit | Suggested priority |
| :--- | :--- | :--- | :--- |
| OPP-01 DDC/CI brightness | ⏳ not started | No `dxva2` P/Invoke in the code base. | Medium |
| OPP-02 Soundboard | ⚠️ built 2026-11-14 (T-14) | `ISoundboardService` + `SoundPlay` / `SoundStopAll` actions; Media Foundation decoding, one WASAPI mixer per device closed after 30 s of silence. Decoding is tested on generated files; **playback on a real device / virtual cable needs a manual test**. OGG not supported. | Medium |
| OPP-03 Timer/Pomodoro/Stopwatch tiles | ⚠️ built 2026-11-14 (T-13) | `ITimerService` (monotonic clock, per profile + page + key), 500 ms tick only while something runs or blinks; no end sound yet (T-14). | Medium |
| OPP-04 Deck screensaver | ⏳ not started | Only the *Windows* screensaver action (`SystemScreensaver`) exists, not a deck screensaver. | Low |
| OPP-05 MCP deck server | ⏳ not started | No `--mcp-deck` switch in `Program.cs`. | Low |
| OPP-06 MIDI / vJoy | ⏳ not started | No WinMM/vJoy code. | Low |
| OPP-07 Profile snapshots | ✅ done 2026-11-13 (T-02) | Daily + before-delete/import/restore/migration snapshots (newest 30), restore with confirmation, corrupt files quarantined as `*.corrupt-<time>`, `SchemaVersion` + migration chain, optional mirror folder, Settings → Backups card. | – |

**Mapping to tasks** ([§3.1](#31-feature-backlog)): OPP-01 → **T-15** · OPP-02 → **T-14** · OPP-03 → **T-13** · OPP-04 → **T-22** · OPP-05 → **T-26** (needs **T-04**, T-19, T-24) · OPP-06 → **T-27** · OPP-07 → **T-02** (+ **T-03** confirmations, schema version).
Recommended order *(replaces §5.4's list)*: **T-35, T-02, T-03, T-36, T-01, T-04, T-05, T-06, T-16, T-11, T-18** (all P1), then the P2 tasks. Rationale: T-02/T-03/T-35 protect user data and hardware; T-01 makes a README claim true; T-04 unblocks CLI/MCP/app contract; T-05/T-06 are the biggest editing-UX gaps.

### 3.1. Feature backlog

Priority: **P1** high value / unblocks others, **P2** good value, **P3** nice to have. Size: **S** < 1 day, **M** 1–3 days, **L** > 3 days. A hardware test is needed where the feature talks to the real deck.

| ID | Feature | Priority | Size | Needs hardware test | Status |
| :-- | :-- | :-: | :-: | :-: | :-- |
| T-35 | Firmware-wedge guard, init handshake, hand-back ordering | P1 | S | yes | Partly done |
| T-02 | Profile backups, corrupt-file quarantine, schema version | P1 | M | – | Done |
| T-03 | In-app confirmations & hold-to-confirm | P1 | S | – | Done |
| T-36 | Installed apps & games catalog (Start menu, Store, Steam/Epic/GOG) | P2 | M | – | Done |
| T-01 | Wire Smart Profiles (auto-switch per app) | P1 | M | – | Partly done |
| T-04 | Local deck API (loopback HTTP + token) | P1 | M | – | Partly done |
| T-05 | Press / Hold / Double-press as separate bindings per control | P1 | M | – | Partly done |
| T-06 | Key editing ergonomics (drag/swap, copy/paste, undo, paste look) | P1 | M | – | Done |
| T-16 | Audio output device switching ("Switch with") | P1 | S | – | Done |
| T-11 | Live Value action / status board | P1 | M | – | Partly done |
| T-18 | LED effect modes, profile edge light, status lights, LED rate limiter | P1 | M | yes | Partly done |
| T-07 | Action-result feedback (red corner on failed press) | P2 | S | – | Done |
| T-09 | Shareable profile package (`.mhdeck`) with images | P2 | M | – | Done |
| T-10 | Ready-made profiles, game presets, "For games" send mode | P2 | M | – | Partly done |
| T-08 | Guided first-run tour + draft/trial mechanism | P2 | M | – | Partly done |
| T-37 | Auto-made profiles for installed apps/games + nudges (opt-in) | P3 | M | – | Partly done |
| T-12 | Conditional looks ("When" rules) + label tokens | P2 | M | – | Partly done |
| T-13 | Timer / Pomodoro / Stopwatch keys (OPP-03) | P2 | M | – | Partly done |
| T-14 | Built-in soundboard (OPP-02) | P2 | M | – | Partly done |
| T-15 | Monitor brightness via DDC/CI (OPP-01) | P2 | S | yes | Planned |
| T-19 | Dial notification / status / question layer | P2 | M | yes | Planned |
| T-24 | CLI client | P2 | S | – | Partly done |
| T-25 | Custom-app status contract + Claude Code status keys | P2 | M | – | Partly done |
| T-26 | MCP servers (`--mcp`, `--mcp-deck`) (OPP-05) | P2 | M | – | Partly done |
| T-29 | Diagnostics page, module test, redacted report | P2 | M | yes | Planned |
| T-30 | Send-pipeline performance (dedupe, coalescing, LED scheduler) | P2 | M | yes | Partly done |
| T-21 | Key look editor upgrade (icon library, styles, badges, motion) | P2 | L | – | Partly done |
| T-33 | Repo hygiene (CHANGELOG, PRIVACY, SECURITY, NOTICE, CI) | P2 | S | – | Done |
| T-17 | Small actions (deck brightness, seek, type-text, app-in-front volume) | P3 | S | – | Partly done |
| T-20 | Lights to the music | P3 | M | yes | Planned |
| T-22 | Deck screensaver (OPP-04) | P3 | M | yes | Planned |
| T-27 | MIDI output + vJoy "sim panel" (OPP-06) | P3 | M | yes | Planned |
| T-31 | Stream Deck host extras | P3 | M | – | Done |
| T-32 | Update checker + What's new | P3 | S | – | Partly done |
| T-23 | Living deck (animated scene behind the keys) | P3 | L | yes | Planned |
| T-28 | Multi-module support (numbered controls, serial→group map) + layout editor | P3 | L | yes | Planned |
| T-34 | Small-improvements grab-bag (independent items) | P3 | S | – | Partly done |

---

## 4. Code Audit 2026-11-12 — Results

Scope: roadmap claims vs. code, security review of file/network entry points, resource/threading review, analyzer pass
(`AnalysisMode=All`), localization key parity. Baseline: the build was already 0 warnings / 0 errors; the repository had **no tests**.

### 4.1. Roadmap verification
All items under §2 have a matching implementation in the code base (checked per feature, e.g. `IsForbiddenReport`, fader deadband, 50/110 ms
encoder acceleration, `SessionSwitch` standby, Twitch raids/commercials/markers, Discord Go Live/PTT, Streamlabs named pipe, NVML/ADL2/PDH/RTSS,
`.mhz` importer, DRM manifest reconstruction). All six locales contain the same 1296 keys, and every key used in code/AXAML exists in `en.json`.
The §1 resource figures (RAM/CPU) could not be verified statically.

### 4.2. Defects fixed in this audit

| # | Area | Defect | Fix |
| :--- | :--- | :--- | :--- |
| F-01 | **Security**: plugin install | *Zip-slip*: `InstallPluginPackageAsync` extracted `..\` entries outside the plugin folder; the plugin directory name came straight from the archive. | Destination validated against the plugins root; escaping entries skipped; shallowest `manifest.json` chosen. |
| F-02 | **Security**: profiles | Profile `Id` (from imported JSON/`.mhz`) was used as a file name, so `..\settings` could overwrite `settings.json` (path traversal). | `IsSafeProfileId` whitelist in `SaveProfile`/`DeleteProfile`/import; unsafe ids are regenerated. |
| F-03 | **Security**: profiles | `SaveProfile` fell back to writing **plaintext credentials** if the encrypting path threw. | Fallback removed; a failure now throws instead of leaking secrets. |
| F-04 | **Security**: WebSocket | Loopback server accepted any `Origin`, so a website open in the user's browser could register as a plugin/PI (Cross-Site WebSocket Hijacking); unbounded message size. | `Origin` must be absent/`null`/loopback; 32 MB message cap; static-file prefix check now requires a path separator. |
| F-05 | **Security**: plugin catalog | Remote catalog `Id` used in a temp file name; HTTP and unlimited-size downloads allowed; partial downloads left in `%TEMP%`. | HTTPS only, sanitized name, 256 MB cap, cleanup on failure. |
| F-06 | **Security**: Spotify login | OAuth callback had no `state` check and reflected `error` into HTML (XSS on localhost). | `state` generated and verified; output HTML-encoded. |
| F-07 | **Data loss** | `ProfileService` reset the default profile's LED colour/brightness to black on **every start** if the user had picked `#00FFCC` (a palette colour). | Legacy migration removed (regression test added). |
| F-08 | **Reliability** | `HardwareWatchdog` blacked out all LCDs/LEDs on any *unobserved task exception* (non-fatal in .NET Core, triggered randomly by GC). | Handler now only marks the exception observed. |
| F-09 | **Functionality** | `INPUT` struct lacked `MOUSEINPUT` in its union (32 B instead of 40 B on x64), so `SendInput` failed and the text-typing fallback never worked. | Union completed. |
| F-10 | **Functionality** (dormant, see B-13) | Smart Profiles switched back to the fallback profile whenever the MasterHub window itself had focus (editing a game profile re-switched it); overlapping timer ticks were possible. | Own process ignored; re-entrancy guard. |
| F-11 | **Functionality** | `ToggleProcessMute` muted only the first audio session of multi-session apps (browsers), so sound kept playing. | New state applied to all sessions of the process. |
| F-12 | **Reliability** | `KeyInteractionEngine`: `ObjectDisposedException` in the hold-repeat loop after key release; a throwing handler killed the repeat loop. | Token captured up-front; handler exceptions isolated. |
| F-13 | **Reliability** | `SendHotkey` could leave Ctrl/Alt/Win "stuck" if an exception occurred mid-sequence. | Modifiers released in `finally`. |
| F-14 | **Reliability** | No single-instance guard (autostart + manual start fought over USB HID and port 28123); `crash.log` overwritten; background-thread crashes not logged. | Named mutex; append-only `crash.log` with size cap; `UnhandledException`/`UnobservedTaskException` logging. |
| F-15 | **Resources** | ~50 native `SKRoundRect`/`SKFontStyle` objects leaked per render pass in `ButtonImageRenderer`/`TrayService`. | `using var`. |
| F-16 | **Hardening** | `.mhz` importer: unbounded recursion on nested archives, unbounded entry size. | Depth ≤ 3, entry ≤ 128 MB. |
| F-17 | **Dependencies** | `Microsoft.Extensions.DependencyInjection` was pinned to a *preview* build. | Updated to stable `10.0.0`. |
| F-19 | **Maintainability** | Three files of ~4k lines (`ButtonImageRenderer`, `CanvasViewModel`, `KeyItemViewModel`) and a 735-line `switch` in `ExecuteKeyAction`. | Split into partial classes by concern; `switch` split into per-category handlers (all 146 `case` labels verified identical). See M-01. |
| F-20 | **UI / i18n** | `de`, `es`, `fr`, `ja` locale files contained double/triple-encoded text (`EspaÃƒÂ±ol`, `ðŸ’¾ Speichern`) – the UI in those four languages showed garbage. | Decoded run-by-run to proper UTF-8 (no BOM); regression tests for mojibake, key and `{n}` placeholder parity. |
| F-21 | **UI / theme** | `CheckBox`, `ToggleSwitch`, `RadioButton`, `TabControl`, `ListBox`, `ProgressBar` took the **Windows system accent colour**, so the look differed per machine. | Fluent palette accent set to the app accent (`App.axaml`); `ListBox`/`TabItem` styled. |
| F-22 | **UI / theme** | Hover/pressed/disabled/active states of every custom button class were overridden by the Fluent template (neutral grey hover, accent border lost). | State rules now target `ContentPresenter#PART_ContentPresenter`; file split into `Buttons*.axaml`. |
| F-23 | **UI / theme** | 8 buttons used style classes that did not exist (`accentBtn`, `accent`, `primaryBtn`, `glassBtn`, `ghostBtn`) and 7 had no class → plain grey Fluent buttons; colour swatches turned grey on hover. | Defined `accentBtn`, `ghostBtn`, `glyphBtn`, `swatchBtn`; classes unified; guard tests added. |
| F-24 | **UI / theme** | ~40 loose hex colours (PluginSettingsWindow used its own off-palette dark scheme; slate greys, 4 different greens, 2 reds). | Semantic tokens in `Colors/Brushes.axaml` (`OnAccent`, `InsetBg`, `AccentInfo`, `AccentCaution`, `CloseHover`); views use `StaticResource`. |
| F-25 | **Crash** | `PluginSettingsWindow` pointed at a non-existent `/Assets/masterhub-logo.ico` → `FileNotFoundException` when opening plugin settings. | Reference removed; test opens the window. |
| F-26 | **UI** | Action name showed its emoji twice in the key inspector (icon + emoji inside the localised name). | `StripLeadingSymbolConverter`. |
| F-27 | **i18n** | ~21 hard-coded Polish strings in the Stream Deck plugin UI (inspector section, store, catalog, plugin settings window). | New `SD_*` keys in all 6 languages. |
| F-28 | **Integration / Twitch** | Emote-only, Sub-only, Slow mode and Clear chat were sent as IRC `/commands`, which Twitch **removed on 2023-02-18** – the keys did nothing while the UI flipped state optimistically. | Helix `PATCH /chat/settings` and `DELETE /moderation/chat` (slow wait 3–120 s); state is no longer flipped when the API rejects. |
| F-29 | **Integration / Twitch** | `broadcaster_id` was always the *token owner*; with a bot account clips/markers/raids went to the bot's channel. IRC lines were classified with `Contains()` – a viewer could type `NOTICE emote_only_on` and change the deck state. | Channel id resolved by login (token owner kept as `moderator_id`); command parsed from IRC line structure; auth failure detected. |
| F-30 | **Integration / Kick** | `SendChatMessage` returned `true` even without a token or on HTTP error; chat modes/clear only flipped local state and posted the literal text `/slow 30` to chat; `CreateClip` returned a fabricated "success". | Official `api.kick.com/public/v1/chat` (`broadcaster_user_id` from `/channels?slug=`), honest results; unsupported operations return `false`; modes read from `ChatroomUpdatedEvent`. |
| F-31 | **Integration / Discord** | Camera and screen share used RPC commands `TOGGLE_VIDEO` / `TOGGLE_SCREENSHARE`, which **do not exist** in Discord RPC – with RPC active these two keys never worked (and the hotkey fallback was skipped). | Always hotkeys now, configurable in settings (`CameraHotkey`, `ScreenShareHotkey`). |
| F-32 | **Integration / YouTube** | Every poll cost ~102 API quota units (`search` = 100) → the 10 000/day quota was gone after ~50 min at the default 30 s interval. | Cached channel id and live video, uploads-playlist discovery (2 units); errors such as `quotaExceeded` are shown instead of looking "offline". |
| F-33 | **Integration / Home Assistant** | `StatusPollIntervalSeconds` ignored (hard-coded 2 s full `/api/states`), overlapping polls, and after one network error the integration stayed disconnected forever. | Interval from settings, re-entrancy guard, automatic reconnect in the poll loop. |
| F-34 | **Integration / misc** | Razer battery: raw level ≤ 100 shown as percent (35 % displayed as 90 %), charging flag read from the wrong field; Spotify ignored HTTP 429 / 401 while polling every 2 s; Streamlabs leaked pending-request entries after timeouts. | Fixed (Razer per openrazer: 0–255 scale + `0x07/0x84`; `Retry-After` backoff; `finally` cleanup). |
| F-35 | **Quality** | No integration tests. | `tests/MasterHub.Core.Tests/Integrations`: real clients against local mock servers (OBS v5, Home Assistant REST+WS, VLC HTTP, Streamlabs JSON-RPC WS) and `HttpMessageHandler` fakes (Twitch Helix, Kick, YouTube); Discord IPC framing. |
| F-36 | **Performance** | `SystemMonitorService` sampled *everything* (PDH counters incl. GPU-engine wildcard instances, NVML/ADL, RTSS FPS, network, disk) every second, even when no key displayed a metric; and its tick doubled as the heartbeat of the Discord/OBS/Twitch/Kick reconnect logic. | `MonitorMetrics` flags: only the groups shown on the current deck view are sampled (tick cost 1.28 ms → 0.001 ms when nothing is shown, measured); monitor off when no key needs it, paused in standby; reconnect moved to its own 15 s timer. |
| F-37 | **Performance** | `NowPlayingService` polled Windows Media (GSMTC) every second regardless of use. | Enabled only while a "Now Playing" key is on the current view. |
| F-38 | **Performance** | `ProcessHelper.IsProcessRunning` (used by OBS/VLC/Discord/Teams/Voicemod/conditions) enumerated all processes per call. | One shared Toolhelp32 snapshot cached for 1.5 s (~40 000× cheaper per cached call). |
| F-39 | **Performance** | In standby (Windows locked) the HID poll loop still woke 100×/s, up to 15 animation timers (10–60 Hz) kept decoding/sending frames, and Spotify (every 2 s) / VLC / JetKVM kept polling. | Poll loop sleeps 100 ms in standby / without devices; animations paused; integration polling stopped and resumed after wake. |
| F-40 | **Performance** | Tray memory trimming every 2 min (cache wipe + blocking aggressive GC + working-set trim) caused periodic CPU spikes and re-rendering without freeing private memory. | Checked every 5 min, runs only after ≥ 10 min cooldown and when the working set grew by > 40 MB (or > 220 MB). |
| F-41 | **Performance** | Home Assistant fetched the full `/api/states` every 2 s although the WebSocket already pushes changes. | Safety-net refresh every 60 s while the WebSocket is live. |
| F-42 | **Performance** | Runtime settings: background GC thread and tiered-PGO instrumentation for an idle, small-heap app. | `ConcurrentGarbageCollection=false`, `TieredPGO=false`. |
| F-43 | **Feature** | New: software macros for Razer (and other) mice with Synapse-like recording, modes and button bindings (§2.6). 36 unit tests (engine, recorder, service routing, persistence) + headless UI tests; hooks start only when a binding exists or while recording (zero idle cost otherwise). | – |
| F-44 | **UX/UI** | Navigation was an unlabeled 72 px icon rail (meaning only in tooltips), page titles were ALL-CAPS and differed from nav names, secondary text (`TextMuted`/`TextDim`) failed contrast, NumericUpDown spinners were black (Fluent default). | Labeled sidebar with groups (Workspace / System), active pill with accent glow, collapsible to icons (remembered in `AppSettings.SidebarCollapsed`); title-bar breadcrumb; shared `PageHeader` on all 6 pages (title = nav label); floating `pagePanel`s (radius 20); Studio filters as a segmented control; Razer hint as a callout; higher-contrast text tokens and slightly larger caption/h2/h3; themed spinners. Headless shell shots (`ShellShotTests`). |
| F-18 | **Quality** | No automated tests. | New `tests/MasterHub.Core.Tests` (43 tests: profile security and persistence, safety guard, fader filter, WebSocket origin, key engine, conditionals, smart-profile rules, zip-slip). |

### 4.3. Integration audit (2026-11-12)

How each integration was checked: **mock** = the real client class talks to a local mock server in an automated test; **fake HTTP** = tests with a recording `HttpMessageHandler`; **review** = code read against the public protocol only; **live** = needs the real service/hardware (not done).

| Integration | Verdict | Verified by | Open points |
| :--- | :--- | :--- | :--- |
| OBS Studio (obs-websocket v5) | ✅ works | mock: Hello/Identify/auth (spec formula), requests, events, wrong password (4009), missing password | – |
| Home Assistant | ✅ works (fixed F-33) | mock REST + WebSocket: auth, entities, `state_changed`, service calls, reconnect | HTTPS/`wss` and sub-path installs untested |
| VLC | ✅ works | mock HTTP: Basic auth with empty user, status JSON, `pl_*`/`fullscreen` commands | Fallback hotkeys (`F`, media keys) act on whatever window has focus |
| Streamlabs Desktop | ✅ works over WebSocket | mock JSON-RPC: auth, scenes, `makeSceneActive`, `sceneSwitched` event | Named-pipe path not tested |
| Twitch | ⚠️ fixed (F-28/F-29) | fake HTTP (Helix: chat settings, clear, clips, channel resolution), IRC parsing | IRC login/chat and Helix scopes need a **live** run. Token needs: `chat:read chat:edit clips:edit channel:manage:broadcast channel:manage:raids channel:edit:commercial moderator:manage:chat_settings moderator:manage:chat_messages` |
| Kick | ⚠️ limited (F-30) | fake HTTP | No public API for chat modes / clear chat / clips → those keys now report failure. Pusher chat feed relies on the unofficial `kick.com/api/v2/channels/{slug}` (Cloudflare may block it) |
| YouTube | ✅ works (fixed F-32) | fake HTTP: discovery, caching, end of stream, API errors | Keyless HTML scraping fallback is inherently fragile |
| Discord | ⚠️ partly (F-31) | IPC framing tests; code review of mute/deafen/voice-channel RPC (`SET_VOICE_SETTINGS`, `SELECT_VOICE_CHANNEL`, `AUTHORIZE`/`AUTHENTICATE`) | RPC flow not run against a real client (a test would touch the user's Discord); PTT is emulated with mute |
| Microsoft Teams | ✅ by review | hotkey set matches Teams defaults (Ctrl+Shift+M/O/K/H) | Needs the meeting window; **live** |
| Spotify | ⚠️ review | PKCE flow, endpoints/scopes reviewed; added `state` (F-06) and 429/401 handling | Not run against Spotify; Developer-mode app + Premium required for playback control |
| Voicemod | ⚠️ review | action names and port list match Control API 1.0.0 | Hard-coded fallback voice list may not match real voice ids |
| JetKVM | ⚠️ review | `/auth/login-local`, `/webrtc/signaling/client`, `setATXPowerAction`/`getATXState` match the device API | Needs a device; TLS validation disabled (B-01) |
| Razer / Wooting | ⚠️ review | Razer 90-byte report layout, XOR CRC and DPI/battery commands match openrazer; Wooting report magic/usage pages match | Needs hardware |
| Windows Media (GSMTC) | ❔ not verified | – | WinRT session API; **live** |
| Stream Deck host | ⚠️ partly | install (zip-slip), Origin filter, manifest handling tested | Plugins in headless Edge/Node need a **live** run (verify the Origin filter) |

---

## 5. Backlog — Remaining Work (not done in the audit)

### 5.1. Defects / risks
- **B-01 JetKVM TLS validation is disabled** (`ServerCertificateCustomValidationCallback => true`). Acceptable for self-signed LAN devices, but should become trust-on-first-use / certificate pinning.
- **B-02 ✅ fixed 2026-11-13 (T-03)** – hardware keys need a 1.5 s hold (opt-out per key), a click in the Studio asks via the in-app overlay. Was: `SystemShutdown`/`SystemRestart`/`SystemSleep` fire on a single key press. Add hold-to-confirm or a confirm tile.
- **B-03 `SecretProtectionService.Protect` silently returns plaintext if DPAPI throws**, and `Unprotect` returns `""` on failure (a later save then overwrites the encrypted blob). It should surface an error and keep the original blob.
- **B-04 About 300 empty `catch { }` blocks** hide failures (profile/settings load, plugin start, rendering). Route them through the log service, at least at debug level.
- **B-05 Polling-loop buffers are reused without clearing** (`MasterHubManager.PollLoop`): the `Length >= N` checks are meaningless on 256-byte buffers, so stale bytes from a previous longer packet can be parsed. Use the actual report length.
- **B-06 `WindowsAudioService` resolves `Process.GetProcessById` per session on every fader tick**; add a short-lived pid→name cache and dispose session controls.
- **B-07 Conditional action "simple format"** (`target?ifTrue:ifFalse`) breaks when a parameter contains `:` or `?` (e.g. URLs). The JSON format is unaffected; deprecate the simple format or escape it.
- **B-08 `PasteText` overwrites the user's clipboard** without restoring it.
- **B-09 ✅ fixed 2026-11-13 (T-02)** – unreadable files are quarantined, never overwritten; backups exist. Was: unreadable files are skipped or overwritten silently (see OPP-07).
- **B-10 Un-disposed resources**: `CancellationTokenSource` fields (`HomeAssistantService._wsCts`, `YouTubeService._pollingCts`, `StreamDeckWebSocketServer._cts`), `Process` objects in `StreamDeckPluginHost`, `EventWaitHandle` in `HardwareWatchdog`; a per-call `HttpClient` in `DiscordService` (use a shared instance).
- **B-11 Sync-over-async / blocking I/O in async paths** (analyzer CA1849: plugin install, catalog item download, `StreamDeckPluginHost` runner start).
- **B-12 Shutdown path uses `GetAwaiter().GetResult()` on the UI thread** (`App.ShutdownHardware`): fine today, fragile if a plugin-host `await` ever captures the UI context.

- **B-13 ✅ fixed 2026-11-13 (T-01)** – rules are fed to the service, `SetWinEventHook` + 250 ms safety net (no hook without enabled rules), Auto-switch toggle (page + tray), Link-app picker; open: "call profile", recently-seen apps. Was: register rules in the profile model, call `UpdateRules`, handle `AutoSwitchProfileRequested`, add a Profiles-page UI. The service polls every 500 ms with `Process.GetProcessById`; when wiring it, prefer `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` (event-driven, zero idle cost) and stop polling when there are no enabled rules. the hook alone misses some activations (Chrome), so add a cheap 250 ms `GetForegroundWindow()` comparison. **README currently advertises this feature as working** (§"Smart Profiles") — fix the README or finish the feature. → **T-01**.

*The items below came from a source review (2026-11-13). They are hypotheses about **our** code until checked: each names what to verify. Hardware numbers were measured on a single deck.*
- **B-14 ✅ guard + handshake done 2026-11-13 (T-35)** – `ModuleSoftwareState` refuses feature reads on keys/dial outside software mode; handshake 600/200 ms, 3 attempts, `ModuleUnresponsive` event; hand-back under the picture lock (`Release`). Open: crash-guard restart + crash report, `pnputil` module restart, exit-behaviour setting, hardware smoke test. Original finding: *reading any feature report on keys or dial in hardware mode wedges the module until power-cycled* (firmware bug, verified 2026-10-09). **Not exposed today** (we read features only from the Faders module: `GetSliderValues`), but `MasterHubDeviceHandle.GetFeatureReport` has no software-mode guard, so serial/diagnostic reads (T-28/T-29) would expose us. Also adopt an init handshake (software mode twice, 600 ms settle, confirm by firmware read, 3 attempts) and an "unresponsive module" state. → **T-35**.
- **B-15 Unclean exit.** The firmware has no watchdog: after a crash keys/dial show the last picture forever (seen frozen overnight). `HardwareWatchdog` extinguishes, but there is no automatic restart (capped, e.g. 3 in 10 min), no in-app crash report, no shutdown-in-progress check, and no verification that hand-back never races an in-flight picture write (a picture sent during hand-back left the dial refusing commands at the next start). → **T-35**.
- **B-16 ✅ rate limiter built 2026-11-13 (T-18)** – `LedCommandScheduler` (own thread, coalescing, diffs, ≥ 40 ms per knobs/faders module, base exempt, stale music commands dropped); all live LED setters of `MasterHubManager` go through it; **needs the hardware smoke test** (see HARDWARE_PROTOCOL.md). Original finding: the faders **dropped off the USB bus at 50 LED updates/s**; the safe approach is to space knob/fader sends ≥ 40 ms, send LED batches as diffs on a thread of their own, and never pass the `persist/save` byte for live updates (ours passes `false` everywhere — keep it). Check every high-frequency LED caller (fader LED colour follows, rainbow, future music lights). → **T-18**.
- **B-17 Panoramic key slicing may be misaligned.** We cut 72×72 tiles with a uniform pitch of 102×100 px from 0 (`ButtonImageRenderer.Panoramic.cs`); the deck's real windows, measured with a camera, are at x = 5/104/203/303/402, y = 1/101/199. Up to ~6 px difference. Verify on hardware (whole-panel `0x02 0x35` vs. 15 tiles) before building more on slicing. → **T-23 / T-34 #17**.
- **B-18 User pictures are stored as picked file paths** (`ImagePath`, `ActiveImagePath`, `CustomIconPath`, panoramic path): a moved/deleted file blanks a key and exported profiles lose their pictures. Copying every picked file into app data under a content-hash name fixes this. → **T-09**.
- **B-19 ✅ verified 2026-11-13 (T-16)** – `WindowsAudioService` resolves the default endpoint on every call (a test guards that nobody adds a cache). Original finding: a volume knob kept controlling the speakers after switching to headphones because only the *old* endpoint was checked. Verify `WindowsAudioService` re-resolves by *device id*. → **T-16**.
- **B-21 ✅ fixed 2026-11-14 – switching profiles overwrote the new profile's keys.** `CanvasViewModel.ApplyProfile` saved the canvas into `GetActiveProfile()`, which after `SetActiveProfile` is already the *new* profile: B's keys on disk were replaced by A's (a later edit healed it; closing the app first lost B's keys). The canvas now remembers which profile it shows (`_loadedProfileId`) and saves there. Found while building T-08; regression tests in `PageNavigationTests`.
- **B-22 ✅ fixed 2026-11-14 – "add page" right after an edit dropped the edit.** `AddNewPage` read the profile before saving the canvas, and `GetAllProfiles()` returns a fresh copy from disk, so the stale copy overwrote the just-saved keys (only the 300 ms auto-save delay hid it).
- **B-20 No change detection / pipelining in the picture send path.** `MasterHubManager.SetKeyImage` writes every call; `WriteOutputReport` waits for each 320-byte report. See P-07/P-09. → **T-30**.

### 5.1a. Performance backlog (measure first)
- **P-01 Software rendering for the main window**: `Win32PlatformOptions.RenderingMode = Software` normally saves tens of MB (no ANGLE/D3D context) for a mostly static UI. Not applied because it needs a before/after check on a real GUI run (the guardrail forbids launching the exe in agent sessions) – try with a `--software-rendering` switch and compare Task Manager *private bytes*.
- **P-02 Event-driven HID loop**: replace `Thread.Sleep(10)` in `MasterHubManager.PollLoop` with `WaitHandle.WaitAny` on the per-device input events (the transport already signals `_dataAvailableEvent`), converting the `loopCount % N` counters to time-based ones. Removes ~100 wakeups/s while devices are connected; needs hardware to validate.
- **P-03 Clock-only decks**: a clock/date key needs one render per *minute*, but the heartbeat ticks every 2 s.
- **P-04 Per-key animation timers** (≤ 15 timers at up to 60 Hz) could share one scheduler thread.
- **P-05 EcoQoS in tray** (`PROCESS_POWER_THROTTLING`): deliberately **not** done – tray mode is exactly when the user presses deck keys, and E-core scheduling would add latency.
- **P-06 ReadyToRun / trimming** for release publishing (lower start-up JIT CPU); startup-only gain.
- **P-07 Pipelined picture writes** *(measured on one deck)*: 8 overlapped writes in flight on a separate `FILE_FLAG_OVERLAPPED` handle took a 40 KB picture from **33.6–109.6 ms / 4.2–6.2 ms CPU** to **21.7–28.2 ms / 2.2–2.5 ms CPU**; chunks of one picture must also go back-to-back under one lock (releasing it between chunks made a 33 KB panel take 3.3 s). Needs a before/after on our hardware; keep a switch to disable it. → T-30.
- **P-08 Device-change notification instead of a timer.** One HID enumeration cost ≈ 150 ms CPU, so rescan only after a `CM_Register_Notification` event for `GUID_DEVINTERFACE_HID` (then 1 s for 10 s, 15 s safety net). We rescan every 2 s (`MasterHubManager.HotPlugBackgroundLoop`, via SetupAPI — **measure `SyncConnectedDevices` first**, our cost may be far lower). → T-30.
- **P-09 Send only what changed.** Keep the panel pixels per module and re-send only differing keys, pace moving panels ≥ 50 ms and the dial ≥ 36 ms (≈ 32 pictures/s measured), and log a 10-s perf line (`keys X/s sent of Y drawn, ms to draw/send, KB each, LEDs/s, engine busy %`). → T-30 / T-29.

### 5.2. Maintainability / tooling
- **M-01 God classes** — *partly done (2026-11-12)*: the three ~4k-line files were split into `partial` classes by concern with no behaviour change (`ButtonImageRenderer` → 12 files, `CanvasViewModel` → 20 files incl. `ExecuteKeyAction` split into 20 per-category `TryExecute…Action` handlers, `KeyItemViewModel` → 11 files; largest part is now ~800 lines). **Still open**: `SystemMonitorService` (2.0k), `SettingsPageViewModel` (1.9k), `MasterHubManager` (1.8k), `StreamDeckPluginHost` (1.7k), `DiscordService` (1.3k); and the single 500-line method `ButtonImageRenderer.CreateCustomKeyJpeg` (`.KeyCompositor.cs`) should become a pipeline of small draw steps. `partial` only reorganises files — real decoupling (e.g. one `IKeyActionHandler` per category, one renderer class per tile family) is the next step.
- **M-05 Remaining visual/i18n debt** (found in the UI audit, not fixed): (a) many `StatusMessage`/log strings in ViewModels are hard-coded Polish; (b) the type scale is fragmented (`FontSize` 8.5/9/9.5/10/10.5/11/11.5/12/13 …) – define 4–5 text classes and migrate views; (c) corner radii vary (1–34) – introduce `Radius*` tokens; (d) `Button` has no global default style (guard test only); (e) key inspector delete glyph renders monochrome (no `emoji` class); (f) no app icon (`/Assets` does not exist) for windows/taskbar; (g) inspectors/pages were rendered headless **without live data** – tile content, real keypad grid and overflow with long localised strings (de/fr/ja) still need a visual pass in the running app.
- **M-02 Test coverage**: integration clients are covered against mocks (see §4.3; missing: Voicemod, Twitch IRC, Spotify, Discord pipe, JetKVM); pure `MasterHub.Core` logic and static UI integrity are covered, plus `tests/MasterHub.Avalonia.Tests` (headless Skia: every view creates/renders, button normal-vs-hover, control gallery; PNGs go to `%TEMP%\mh_shots` or `MH_SHOTS`). Missing: `MasterHubManager` with a fake HID transport, integration clients against local mock servers (OBS/Home Assistant/Spotify), ViewModel tests (Avalonia headless).
- **M-03 CI**: add a GitHub Actions workflow (`build` + `test` on `windows-latest`).
- **M-04 Hot-path allocation**: `WriteOutputReport` zeroes the OVERLAPPED struct byte-by-byte on every write (use `Span.Clear`).

### 5.3. Needs a hardware / GUI smoke test (not verifiable in the audit)
**Macros first**: bind a macro to a Razer side button and check (a) the default Back/Forward action is swallowed, (b) the "Razer mouse only" filter really ignores a second mouse (it relies on the last Raw Input device, not on the hook), (c) key presses injected with `SendInput` reach games that read scan codes, (d) recording + Stop click trimming.
USB HID read/write on every module, panoramic stream timing (~11 ms), dock orientation switch, standby on lock/unlock, Stream Deck plugins in headless Edge/Chrome and Node (verify that the new `Origin` filter accepts the runner's origin), the Spotify login flow (new `state` parameter), plugin install from the catalog (HTTPS-only), and the memory/CPU figures from §1.

### 5.4. Planned features
OPP-01 … OPP-07 above **plus 30 further tasks** (editing UX, live values, local API/CLI/MCP, LED effects, multi-module, diagnostics, …) — all listed with priority, size and status in [§3.1](#31-feature-backlog). Use that order (see §3); the earlier order *OPP-07, OPP-02, OPP-03, OPP-01, OPP-04, OPP-05, OPP-06* still holds *within* the OPP items.

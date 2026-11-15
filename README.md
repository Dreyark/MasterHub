# Cooler Master MasterHub .NET Control Deck

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia 12](https://img.shields.io/badge/Avalonia-12.1-8E44AD?style=for-the-badge&logo=avalonia&logoColor=white)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows_x64-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://microsoft.com/windows)
[![Architecture](https://img.shields.io/badge/Architecture-MVVM_Clean_Code-00DFD8?style=for-the-badge)](docs/ARCHITECTURE.md)
[![Security](https://img.shields.io/badge/Security-Windows_DPAPI-00C853?style=for-the-badge)](docs/ARCHITECTURE.md#47-security--dpapi-encryption-secretprotectionservice)
[![License](https://img.shields.io/badge/License-MIT-7928CA?style=for-the-badge)](LICENSE)

An independent, native **C# (.NET 10 & Avalonia 12)** control deck and hardware driver interface for the **Cooler Master MasterHub** modular stream deck and productivity workstation. This is an unofficial community project (see [Disclaimer](#-disclaimer)).

A native desktop application written from the ground up (no Electron, no web view): a single process with a small memory footprint, a hardware driver layer talking USB HID directly, a **stream deck action engine**, a **WASAPI audio mixer** and **deep hardware/software integrations**.

---

<p align="center">
  <img src="docs/App.png" alt="MasterHub Studio Application Interface" width="100%" />
</p>

---

## ⚡ Why MasterHub .NET?

- **One native process**: no bundled browser engine (helper processes exist only for Stream Deck plugins you run). On the development PC it idles at roughly 30–40 MB of RAM and under 1 % CPU (figures are indicative and not part of an automated test).
- **Direct USB HID**: pure Win32 HID transport with a dedicated ~15 ms polling thread; no vendor DLLs and no C++ runtimes.
- **Per-app audio**: native WASAPI / CoreAudio session control for the faders.
- **Robust hot-plugging**: modules can be docked and removed while the app runs.
- **Private by design**: 100 % local, no telemetry, no account; secrets are encrypted with Windows DPAPI. Every outbound connection is listed in [PRIVACY.md](PRIVACY.md).

---

## 🎛️ Supported Hardware Modules

All modules connect magnetically to the MasterHub Base Dock and communicate via USB Vendor ID `0x2516`:

| Module | Model | Product ID | Capabilities Supported |
| :--- | :---: | :---: | :--- |
| **Base Station** | `MHBS01` | `0x01F6` | Magnetic bus powering, unified LED strip brightness & RGB color control, standby management, tilt orientation sensor (Feature `0x35`: 1=Horizontal, 0=Vertical) |
| **Key Deck 15** | `MHKD15` | `0x01FE` | 15 LCD color displays (72×72), real-time SkiaSharp graphics, animated GIF/MP4 loop decoding, dual-state toggles, panoramic wallpaper engine (`0x02 0x35`) |
| **Fader 5** | `MHFD05` | `0x0200` | 5 vertical faders (0–100%), per-channel RGB LEDs & brightness, analog deadband filtering, per-app WASAPI mixer |
| **3 Knobs** | `MHKB03` | `0x0202` | 3 rotary encoders with click switches, dynamic acceleration (1×/2×/3×), dedicated RGB LED rings (10 modes), volume/scrub/macro controls |
| **Roller 2** | `MHRR02` | `0x0204` | 2 dual-direction rotational dials with push buttons (Volume, Scroll, Track scrub, DPI/Profiles). *(Hardware has no LEDs)* |
| **Dial Display** | `MHDD01` | `0x0206` | High-resolution circular encoder with integrated central 240×240 IPS color display (`0x02 0x45` frame stream) and click switch |
| **IPS Display** | `M131` | `0x131E` / `0x1312` | Ultra-wide monitoring screen module detection |

---

## ✨ Features & Capabilities

### ⌨️ Stream Deck Action Engine & 72×72 LCD Graphics
- **Physical LCD Graphics**: Renders high-contrast 72×72 action cards with icons, custom text, and backgrounds using SkiaSharp and streams them directly to physical button displays.
- **Pre-Cached Zero-Latency Rendering**: Zero memory allocations or garbage collection stutter on button clicks.
- **Dual-State LCD Graphics**: Set two distinct graphics per key (**Default / State 1** and **Active / State 2**) for toggle actions like Mute/Unmute or Play/Pause, dynamically updating physical LCD pads in real time.
- **Animated Key Icons**: Supports animated GIF and MP4 loops with hardware-accelerated MediaFoundation decoding and cached frame playback.
- **Multi-Actions & Sequences**: Execute chains of actions with configurable delays (e.g. switch OBS scene, launch game, set audio volumes, mute mic).
- **Conditional Actions**: Execute different sub-actions based on application or system states.
- **Key Gestures**: Distinct triggers for Single Tap, Double Tap, Long Press, and Hold.

### 🎧 Windows Audio Mixer Subsystem (WASAPI / CoreAudio)
- **Assign Any Fader**: Bind each of the 5 hardware sliders to **Master Volume**, **Microphone Input Level**, or any running process/game (e.g., Spotify, Discord, Chrome, CS2).
- **Auto-Discovery of Audio Sessions**: Dynamic scanning of Windows audio endpoints to automatically populate active apps into dropdown selectors.
- **Instant Mute Toggles**: Dedicated deck keys toggle system or app mute states instantly.

### 🔄 Multi-Function Rotational Dials (Rollers)
- **Master Volume Dial**: Turn clockwise to increase volume, counter-clockwise to decrease volume.
- **Mouse Wheel Scrolling**: Smooth vertical or horizontal scrolling without touching the mouse.
- **Track Scrubbing**: Skip to next/previous tracks with a quick flick of the dial.
- **Hardware Profile Cycling**: Spin dials to step through DPI stages, Polling Rates (Hz), or Wooting keyboard profiles.

### 🧩 Elgato Stream Deck Plugin Ecosystem
- **Universal Plugin Host**: Full compatibility layer running official `.streamDeckPlugin` packages without requiring Elgato software.
- **Bi-directional WebSocket Loopback**: Standard Stream Deck registration protocol with bidirectional event routing (`keyDown`, `keyUp`, `willAppear`, `willDisappear`, `propertyInspectorDidAppear`).
- **Headless Browser Runner**: Puppeteer / Playwright headless Chromium/Edge integration executing HTML-based plugin scripts and canvases in background instances.
- **Dynamic SkiaSharp Compositor**: Real-time rendering of SVG, base64 PNG/JPEG, and HTML Canvas frames streamed from plugins directly onto 72×72 LCD displays.
- **Node.js & Native Runtimes**: Automatic execution and lifecycle management of Node.js and native binary plugins.
- **Plugin Store & Browser**: Search, inspect, install, configure, and remove plugins directly within MasterHub Studio.

### 🔌 Third-Party Integrations & Streaming Suite
- **Home Assistant**: Direct REST and live bidirectional WebSocket event-stream integration (Toggle lights, switches, triggers, sensors, and dial/roller light brightness).
- **OBS Studio**: Direct WebSocket v5 integration (Record, Stream, Scene Switch, Source Mute, Replay Buffer, Studio Mode Toggle & Transitions).
- **Streamlabs Desktop**: JSON-RPC 2.0 via WebSocket port 59650 and Named Pipe `\\.\pipe\slobs` for fast scene switching.
- **Twitch**: Helix API & chat client (Emote-Only, Slow Mode, Sub-Only, Clear Chat, Send Message, Clips, Markers, Raids, Commercials).
- **Kick**: Pusher WebSocket & Kick API (Chat modes, clear chat, send messages, create clips).
- **YouTube Live**: Official YouTube Data API v3 and keyless public scraping fallback for live status and concurrent viewer counter.
- **Discord**: Discord Local IPC RPC integration for microphone mute, deafen, camera toggle, screen share (Go Live), Push-To-Talk, and voice channel join.
- **Microsoft Teams**: Desktop meeting controls (Mute, Camera, Raise Hand, Leave Meeting) with automatic window focus restoration.
- **Spotify**: Web API playback control (Play/Pause, Next/Prev, Volume, Shuffle, Repeat, Like Track, Add to Queue, Play URI).
- **Voicemod**: Official Control API 1.0.0 (WebSocket + REST) with real-time voice changer toggle, "Hear Myself" feedback, live voice switching with soundboard, background voice FX toggle, and microphone mute.
- **JetKVM**: Remote KVM switching, mouse/keyboard forwarding, HDMI video toggle, ATX power short/long press, and hardware reset.
- **VLC Media Player**: HTTP remote control (Play, Pause, Stop, Volume, Next/Prev, Fullscreen).
- **Windows Media (GSMTC)**: Universal Now Playing engine extracting track title, artist, album, and artwork from Spotify, YouTube, VLC, and system media players without requiring API keys.

### 🖥️ Multi-Engine Hardware Telemetry & System Utilities
- **Direct GPU Telemetry**: DXGI (`dxgi.dll`) enumerates all physical GPUs; NVML (`nvml.dll`) extracts NVIDIA GeForce/RTX core temp and GPU load; AMD ADL2 (`atiadlxx.dll`) queries AMD Radeon and AMD Ryzen SoC/CPU metrics.
- **Real-Time Gaming FPS Counter**: RTSS Shared Memory (`RTSSSharedMemoryV2`) coupled with 3D foreground process detection for accurate gaming FPS (strictly 0 FPS on desktop).
- **System Counters**: Win32 PDH (`pdh.dll`) for CPU utility, PhysicalDisk read/write MB/s, and GPU 3D engine load; `GlobalMemoryStatusEx` for RAM and `NetworkInterface` for network speed.
- **LibreHardwareMonitor Fallback**: HTTP JSON reader (`http://127.0.0.1:8085/data.json`) for local hardware monitor servers.
- **Windows High-Res Icon Extractor**: Direct Win32 extraction (`SHDefExtractIconW`) pulling crisp, native 256×256 app icons for shortcuts without blurry scaling.
- **Cooler Master Profile Importer**: Seamless import and parsing of official MasterHub `.mhz` profiles, buttons, faders, knobs, and media assets.
- **Panoramic Wallpaper Engine**: Seamless 480×272 panoramic imagery sliced and streamed across Key Deck 15 LCDs via hardware command `0x02 0x35`.

### 🗔 Windows Desktop & Workflow Productivity
- **Virtual Desktops**: Switch Next/Previous, Create new (`Win+Ctrl+D`), and Close current (`Win+Ctrl+F4`).
- **Desktop Window Management**: Task View (`Win+Tab`), Show Desktop (`Win+D`), and Clipboard History (`Win+V`).
- **Multi-Page & Nested Folders**: Unlimited pages (`GoToPage`, `NextPage`, `PreviousPage`) and folder hierarchies with automatic return buttons (`GoBack`, `GoHome`).
- **Multi-Actions & Conditionals**: Execute complex sequential macro chains with delays or evaluate runtime conditional rules (IF process running THEN action ELSE action).

### 🐍 Direct Hardware Device Drivers
- **Universal Razer Mouse HID**: Direct sensor control via 90-byte Feature Reports. Change DPI stages (400–30,000 DPI), Polling Rate (125–8000 Hz), active onboard profiles (1..5), and read real-time battery status.
- **Wooting Analog Keyboard Driver**: Direct ARM V3 HID control (`UsagePage = 0x1337 / 0xFF55, Usage = 0x0001`). Switch between Digital Mode and Analog Profiles 1–3 (Rapid Trigger, Tachyon Mode, custom actuation).

### 🎯 Smart Profiles & Workspace Management
- **App-Aware Auto-Switching**: MasterHub detects the active foreground application and automatically activates matching profiles (e.g., automatically switches to Premiere profile when video editing, CS2 profile when gaming, or OBS profile when streaming).
- **Unlimited Custom Profiles**: Create, clone, rename, and export profiles with ease.
- **Automatic Backups & Safe Loading**: daily snapshots plus one before every delete, import or restore (Settings → Backups, newest 30 kept, optional OneDrive/Dropbox mirror folder). Unreadable profile files are set aside as `*.corrupt-<time>` instead of being overwritten.
- **Command line (`masterhub-cli.exe`)**: built next to `MasterHub.exe`; talks to the *running* app over the local API (never to the deck directly). `status`, `profiles` (`*` active, `^` Home), `profile <name or id>` (a name that fits two profiles is refused, never guessed), `key <1-15> --label … --color #rrggbb [--action NAME --param …] [--clear] [--page N] [--profile NAME]` (a key without `--action` keeps what it does; keys are numbered left to right, top to bottom), `lights <rainbow | color #rrggbb | profile | off>` (until another profile is applied), `doctor`, and `--json` for `status` / `profiles`. Exit code 0 = done, 1 = not done with the reason printed; "MasterHub is not running" is exit 1 too. Example: a scheduled task that runs `masterhub-cli profile "Focus"`, or a build script that ends with `masterhub-cli key 15 --label "Build OK" --color #00aa00`.
- **AI assistants (MCP)**: `masterhub-cli --mcp` is a Model Context Protocol server: an assistant can read profiles, change keys and set the lights. The limits (no arbitrary service calls, no scripts, closing things only on a hold) are enforced by the app, not by a prompt. See [docs/MCP.md](docs/MCP.md).
- **Claude Code on the deck**: `masterhub-cli --claude-hook` (called by Claude Code hooks) shows whether a session is working, waiting for you or done: a key can change colour/label with a "When" rule or the `{status}` token, and the edge light pulses orange when something waits for you. Only the session id, the project folder name, the event and the tool name of a permission are sent, never prompts or paths. Other apps can push the same six states with `POST /api/state`. See [docs/CLAUDE_CODE_HOOKS.md](docs/CLAUDE_CODE_HOOKS.md).
- **Local API**: a loopback-only HTTP API (`127.0.0.1`, per-install bearer token) lets scripts read state, activate profiles and edit keys; every write is validated all-or-nothing and snapshotted first.
- **Setup Tour & Safe Previews**: on a new install the app offers a one-minute guided setup (one question, a look at your programs, a deck built from a ready-made profile). The result is a *draft*: it is shown on the deck and in the window without being saved, **the keys run nothing** (a press only says what it would do), and your own profile is never touched. Keep or drop each key, then *Keep* it as a profile or *Throw away*. A ready-made profile can also be tried for 20 seconds. A draft left by a crash is offered back at the next start. The tour can be run again from Settings.
- **Assistant (opt-in)**: asked once, and **off until you say yes** (one switch in Settings turns all of it off). It makes *switched-off* profiles for programs it finds on this PC (OBS, Discord, Teams…) and for a game the first time it comes to the front, listed under "Ready for your apps" with a *Switch on* button; a profile you delete is never made again. It also counts how long each program is in front (one stretch counts at most 15 minutes) to suggest a profile for a program you use a lot, suggests a sketch when a new kind of module is connected for the first time and – only if you turn on press counting – points out a key that is never pressed. A suggestion only opens a preview you can keep or throw away, and each one is shown once. The numbers are kept in `usage.json` / `presses.json` next to your profiles, never sent anywhere, and can be deleted in Settings.
- **Soundboard Keys**: a key plays a WAV, MP3, M4A, AAC, WMA or FLAC file on the Windows default device or on any playback device, including a virtual audio cable that feeds Discord or OBS. Choose per key how a second press behaves (play/stop, restart, overlap) and the volume (up to 200 %). The audio output is opened by the first sound and closed again after 30 s of silence. Sound file paths are never exported with a profile.
- **Timer, Pomodoro & Stopwatch Keys**: a key can count down, run Pomodoro work/break phases with colour shifts, or time with splits. Press starts or pauses, hold resets, double-press adds a minute (a split on a stopwatch). A finished timer blinks red, timers keep running on other pages and profiles, and a small panel lists them with a Stop button.
- **Conditional Looks**: a key can change colours or label while a condition holds (CPU/RAM/GPU load or temperature, music playing, microphone or speakers muted, a program in front, time of day). Rules are layers, metric rules do not flicker around their limit, and labels accept tokens such as `{cpu}`, `{time}` or `{song}`. Nothing is sampled unless a key on screen uses it.
- **Icon Library**: 2 359 searchable [Lucide](https://lucide.dev) icons in any colour become the key picture; pictures fill the whole LCD key.
- **Small Actions**: type text without the clipboard, seek with the player's own keys, volume and mute of the program in front, push-to-talk / push-to-mute, "Call a URL", knob and dial modes, deck brightness on a knob.
- **Stream Deck plugin extras**: compatibility badge with plain reasons, CPU flag for busy plugins, Node.js resolved by the version a plugin needs (downloaded only after you agree, checksum verified), multi-state actions ([docs/STREAMDECK_COMPAT.md](docs/STREAMDECK_COMPAT.md)).

### 🔒 Security, Privacy & DPAPI Encryption
- **100% Local & Private**: Zero cloud telemetry, zero remote tracking, zero analytics beacons.
- **Windows DPAPI Encryption**: All sensitive tokens, passwords, and API keys (OBS, Twitch, Spotify, Discord, JetKVM, Voicemod, Streamlabs, YouTube) are encrypted on disk using **Windows DPAPI** (`ProtectedData.Protect` with app-level entropy) and stored with the `enc:v1:` prefix.
- **Hardware Safety Filter (`IsForbiddenReport`)**: Blocks hazardous opcodes (firmware updates, factory resets, tilt sensor decalibration `0x02 0x2F`, SPI flash writes).
- **Safe Profile Export**: Exporting profiles automatically strips passwords and credentials to ensure safe sharing with the community.
- **Clean Repository**: Profiles and settings reside in `%APPDATA%\MasterHub` and are strictly excluded from git tracking.

### 🌐 Multilanguage (i18n)
- Out-of-the-box multilingual support: **Polish (PL)**, **English (EN)**, **German (DE)**, **Spanish (ES)**, **French (FR)**, **Japanese (JA)**.
- Instant runtime switching without restarting the application.
- Easy community translation by placing JSON locale files in `%APPDATA%\MasterHub\Languages\`.

---

## 🏛️ Architecture & Clean Design

```mermaid
flowchart TD
    subgraph UI ["Presentation Layer (MasterHub.Avalonia - Avalonia 12)"]
        W["MainWindow / TitleBarView"]
        NAV["NavigationSidebarView"]
        CAN["ModuleCanvasView / Keys, Sliders, Rollers, Base Views"]
        INS["InspectorPanelView / Action Library & Key Settings"]
        PAG["Pages: Profiles, Action Store, Settings, Logs"]
        
        VM["MainViewModel & Modular Child ViewModels"]
        W <-->|Data Binding| VM
        NAV <-->|Data Binding| VM
        CAN <-->|Data Binding| VM
        INS <-->|Data Binding| VM
        PAG <-->|Data Binding| VM
    end

    subgraph Core ["Hardware & Domain Core (MasterHub.Core)"]
        VM -->|Hardware Management| IMGR["IMasterHubManager (MasterHubManager)"]
        VM -->|Audio Control| IAUD["IAudioService (WindowsAudioService)"]
        VM -->|System Keys & Input| ISYS["ISystemControlService (Win32)"]
        VM -->|Profiles & Settings| IPRF["IProfileService (DPAPI Encrypted)"]
        VM -->|App Auto-Switching| ISMT["ISmartProfileService"]
        VM -->|Integrations| INT["OBS / Streamlabs / Twitch / Kick / YouTube / Discord / Teams / Spotify / Voicemod / HA / JetKVM / VLC"]
        VM -->|Device Drivers| DRV["Razer HID / Wooting ARM V3"]
        VM -->|Action Engine| ACT["KeyInteractionEngine & MultiActions"]
        
        IMGR -->|72x72 Graphics Cache| RND["KeyRenderer & ButtonImageRenderer (SkiaSharp)"]
        IMGR -->|Dedicated ~15ms Thread| POLL["PollLoop (~66Hz Hardware Polling)"]
        IMGR -->|Background PnP Loop| HOTPLUG["HotPlugBackgroundLoop (SetupDi / WMI)"]
        POLL -->|Direct Win32 HID Transport| NAT["MasterHubUsbTransport (Pure C#)"]
    end

    subgraph OS ["Operating System & Peripherals"]
        NAT <-->|USB HID 0x2516| DEV["Cooler Master MasterHub Modules"]
        IAUD <-->|CoreAudio / WASAPI| WAS["Windows Audio Subsystem"]
        ISYS <-->|SendInput / P/Invoke| KBD["Windows Input System"]
        IPRF <-->|DPAPI DP / Local Storage| APPDATA["%APPDATA%\\MasterHub"]
    end
```

---

## 🚀 Getting Started

### Download
A ready-to-run Windows build (self-contained, no .NET needed) is attached to every [release](https://github.com/Dreyark/MasterHub/releases): unzip it and start `MasterHub.Avalonia.exe`. The zip comes with a SHA-256 file. Releases below 1.0 are pre-releases.

### Prerequisites
- **Windows 10 / 11 (x64)**
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- *(No proprietary DLLs, official software, or C++ runtimes required!)*

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/Dreyark/MasterHub.git
   cd MasterHub
   ```

2. Build the solution using the .NET CLI:
   ```bash
   dotnet build MasterHub.slnx --nologo
   ```

3. Run the unit tests (no hardware required):
   ```bash
   dotnet test MasterHub.slnx --nologo
   ```

4. Run the application:
   ```bash
   dotnet run --project src/MasterHub.Avalonia
   ```

---

## 📖 Technical Documentation

- [AI Context & Technical Guidelines](AI_CONTEXT.md) — Single source of truth for AI agents and developers: constraints, coding rules (<150 lines per UI file), and architectural map.
- [Architecture & Design Details](docs/ARCHITECTURE.md) — Multi-threaded hardware polling, MVVM design, Stream Deck plugin runtime, audio subsystem, DPAPI encryption, and hot-plug handling.
- [Hardware Protocol Reference](docs/HARDWARE_PROTOCOL.md) — Reverse-engineered USB HID protocol specifications, device IDs, buffer layouts, dynamic acceleration, and display packet structures.
- [Changelog](CHANGELOG.md), [Privacy](PRIVACY.md) (every outbound connection), [Security](SECURITY.md), [Third-party notices](NOTICE.md), [Contributing](CONTRIBUTING.md).
- [Claude Code hooks & status contract](docs/CLAUDE_CODE_HOOKS.md)
- [Product Roadmap](docs/ROADMAP.md) — Completed milestones, architecture overview, feature backlog and future blueprints.

---

## 🗺️ Roadmap & Implemented Modules

- [x] Base Station, Key Deck 15, Fader 5, and Roller 2 support
- [x] 3 Knobs (`0x0202`) and Dial Display (`0x0206`) hardware module drivers
- [x] Base Station tilt orientation sensor (Feature `0x35`: 1=Horizontal, 0=Vertical)
- [x] Hardware Safety Guard (`IsForbiddenReport`) blocking dangerous opcodes & SPI flash burn
- [x] Zero-latency native P/Invoke pipeline (~15ms, ~66Hz dedicated polling thread)
- [x] Hot-plugging & dynamic magnetic dock reconfiguration
- [x] Unified multi-module standby power management
- [x] Full Windows CoreAudio / WASAPI mixer per fader (Master, Mic, Processes)
- [x] Analog deadband filtering & rotary encoder dynamic acceleration (1×/2×/3×)
- [x] SkiaSharp 72×72 LCD graphics engine with zero-allocation memory caching
- [x] Animated key displays (GIF & MP4 loop playback via MediaFoundation)
- [x] Panoramic 480×272 LCD wallpaper engine (`0x02 0x35`)
- [x] Elgato Stream Deck plugin runtime (WebSocket loopback 28123, Headless Chromium/Edge runner, Node.js runner)
- [x] DRM manifest auto-reconstruction & dynamic SkiaSharp SVG/Canvas/Text live compositor
- [x] Home Assistant smart home integration (REST & WebSocket live states)
- [x] Cooler Master official `.mhz` profile importer
- [x] Direct Multi-Engine Telemetry (DXGI, NVML, AMD ADL2, PDH, and RTSS 3D gaming FPS counter)
- [x] Windows native high-resolution app icon extraction (`PrivateExtractIconsW` / `SHDefExtractIconW`)
- [x] Windows Virtual Desktops (Next, Prev, Create, Close) & Desktop Tools (Win+Tab, Win+D, Win+V)
- [x] Mouse & keyboard macros (record, edit, bind to mouse side buttons / keys / deck keys; Synapse-style software macros)
- [x] Rotational dial control for Volume, Wheel scroll, and Track skip
- [x] Universal Razer mouse hardware driver (DPI stages, Polling Rate, Profiles, Battery)
- [x] Native Wooting analog keyboard driver (ARM V3 HID, Profiles 0..3 switching)
- [x] OBS Studio WebSocket v5 integration (Record, Stream, Scenes, Virtual Cam, Studio Mode)
- [x] Streamlabs Desktop integration (JSON-RPC 2.0 via WebSocket 59650 and Named Pipe `slobs`)
- [x] Twitch & Kick chat, clips, markers, moderation, raids, commercials
- [x] YouTube Live viewer counter and status telemetry
- [x] Discord Local IPC RPC (Mute, Deafen, Camera, Go Live, PTT, Voice Channel Join)
- [x] Microsoft Teams desktop meeting controls (Mute, Camera, Raise Hand, Leave Meeting)
- [x] Spotify Web API playback, shuffle, repeat, queue, URI
- [x] Voicemod Control API 1.0.0 integration (WebSocket + REST, voices, soundboard, FX toggle)
- [x] JetKVM ATX power control and VLC Media Player HTTP control
- [x] Windows Media (GSMTC) Now Playing track title, artist, album, artwork extractor
- [x] Multi-Actions, Delays, and Conditional Logic
- [x] Multi-Page & nested folder navigation with automatic return key
- [x] Smart Profiles (foreground window automatic profile switching)
- [x] Windows DPAPI hardware-backed encryption for all stored secrets (`enc:v1:`)
- [x] Full runtime multilanguage support (PL, EN, DE, ES, FR, JA)
- [x] Modern Avalonia 12 desktop presentation layer

---

## 📌 Disclaimer

MasterHub .NET is an independent project. It is **not affiliated with, endorsed by or supported by** Cooler Master, Elgato / Corsair, Razer, Wooting or any other company named in this repository. Product and company names are trademarks™ or registered® trademarks of their respective owners and are used only to say which hardware and services the software works with. The USB protocol notes in [docs/HARDWARE_PROTOCOL.md](docs/HARDWARE_PROTOCOL.md) come from observing the hardware's behaviour; the software is provided "as is" (see the licence) and you use it with your own devices at your own risk.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).  
All product and company names are trademarks™ or registered® trademarks of their respective holders. Cooler Master, MasterHub, Razer, and Wooting are trademarks of their respective owners.

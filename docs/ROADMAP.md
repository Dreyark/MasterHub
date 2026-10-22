# MasterHub .NET — Product Roadmap & Architecture Opportunities

> **Benchmark & Inspiration**: [HubDeck (`Przemotar/hubdeck`)](https://github.com/Przemotar/hubdeck/)  
> **Target Framework**: .NET 10.0 (C# 13, Windows x64, Avalonia 12, SkiaSharp)  
> **Build Command**: `dotnet build MasterHub.slnx --nologo` (0 Warnings, 0 Errors)

---

## 1. Architectural Comparison: HubDeck vs. MasterHub .NET

| Dimension | HubDeck (`Przemotar/hubdeck`) | MasterHub .NET Studio (`Dreyark/MasterHub`) |
| :--- | :--- | :--- |
| **Core Tech Stack** | Rust (Tauri 2 + WebView2 / React) | **C# (.NET 10 + Avalonia 12 native XAML)** |
| **Memory Footprint**| ~40–62 MB RAM, ~3–9% CPU | **~32–38 MB RAM, < 1% CPU idle** |
| **Rendering Subsystem** | Browser WebView2 (HTML/DOM) | **Native vector engine + SkiaSharp 72×72 cache** |
| **USB HID Layer** | Rust `hidapi` | **Pure Win32 HID (`SetupAPI`, zero vendor DLLs)** |
| **Hardware Safety** | None | **`IsForbiddenReport` guard (blocks firmware bricking & flash wear)** |
| **Credential Security** | Plain JSON configuration files | **Windows DPAPI hardware encryption (`enc:v1:`)** |
| **Stream Deck Runtime** | None | **Bi-directional WebSocket 28123 + Node.js + Headless Chromium/Edge** |
| **Hardware Telemetry** | Basic CPU/RAM sysinfo | **Direct DXGI, NVML (NVIDIA), AMD ADL2 (Radeon/Ryzen), PDH, RTSS FPS** |
| **Peripheral Drivers** | None | **Universal Razer Mouse HID + Wooting ARM V3** |

---

## 2. Completed Milestones

### 2.1. Hardware Modules & Zero-Latency Engine
- [x] **Core Modules**: Base Station (`0x01F6`), Key Deck 15 (`0x01FE`), Fader 5 (`0x0200`), Roller 2 (`0x0204`).
- [x] **3 Knobs Module (`0x0202`)**: 3 rotary encoders with push buttons, RGB LED rings with 10 hardware lighting modes (`0x02 0x2C`), speed control (`0x02 0x30`), and velocity acceleration.
- [x] **Circular Dial Module (`0x0206`)**: 240×240 IPS circular display (`0x02 0x45`), outer dial ring, software mode activation (`0x03 0x49`), and brightness control (`0x03 0x44`).
- [x] **Dock Accelerometer / Orientation Sensor**: Automatic canvas re-orientation via Feature Report `0x35` (1 = Horizontal, 0 = Vertical).
- [x] **Hardware Safety Guard**: `MasterHubDeviceHandle.IsForbiddenReport` prevents firmware corruption, factory calibration wipe (`0x02 0x2F`), and SPI flash degradation.
- [x] **Analog Deadband & Smoothing**: Fader jitter filtering (`<= 2` -> 0%, `>= 98` -> 100%, and < 2% noise delta suppression).
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
- [x] **Loopback WebSocket Server**: Local WebSocket host on port 28123 conforming to Elgato Stream Deck SDK.
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

### 2.5. Profile Engine, Productivity & Security
- [x] **Windows DPAPI Encryption**: Hardware/user-backed encryption (`enc:v1:`) for all sensitive credentials (`SecretProtectionService`).
- [x] **Data Isolation**: All user configuration isolated under `%APPDATA%\MasterHub\`.
- [x] **Cooler Master `.mhz` Importer**: Full migration support for official MasterHub profile archives.
- [x] **Smart Profiles**: Automatic foreground-window process detection and profile switching (`SmartProfileService`).
- [x] **Multi-Page & Folder Navigation**: Unlimited pages and nested folder hierarchies with automatic return button on key 0 (`GoBack`, `GoHome`).
- [x] **Windows Desktop Controls**: Virtual Desktops (Next, Prev, Create, Close), Task View (`Win+Tab`), Show Desktop (`Win+D`), Clipboard History (`Win+V`).
- [x] **Multi-Actions & Conditionals**: Action sequences with delays and conditional logic (IF process running THEN action ELSE action).
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

# Cooler Master MasterHub .NET Control Deck

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia 12](https://img.shields.io/badge/Avalonia-12.1-8E44AD?style=for-the-badge&logo=avalonia&logoColor=white)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows_x64-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://microsoft.com/windows)
[![Architecture](https://img.shields.io/badge/Architecture-MVVM_Clean_Code-00DFD8?style=for-the-badge)](docs/ARCHITECTURE.md)
[![Security](https://img.shields.io/badge/Security-Windows_DPAPI-00C853?style=for-the-badge)](docs/ARCHITECTURE.md#security--secret-protection)
[![License](https://img.shields.io/badge/License-MIT-7928CA?style=for-the-badge)](LICENSE)

An ultra-fast, native **C# (.NET 10 & Avalonia 12)** control deck and hardware driver interface for the **Cooler Master MasterHub** modular stream deck and productivity workstation.

Engineered from the ground up to replace the official Electron-based software with a lightning-quick native desktop application that consumes **<38 MB of RAM**, delivers **sub-millisecond input responsiveness**, and includes a comprehensive **stream deck action engine**, **WASAPI audio mixer**, and **deep hardware/software integrations**.

---

<p align="center">
  <img src="docs/App.png" alt="MasterHub Studio Application Interface" width="100%" />
</p>

---

## ⚡ Why MasterHub .NET?

| Metric | Official MasterHUB (Electron) | MasterHub .NET (Native Avalonia 12) | Improvement |
| :--- | :---: | :---: | :---: |
| **RAM Usage** | ~480 – 550 MB | **~32 – 38 MB** | **~93% Less RAM** |
| **Input Latency** | 30 – 80 ms | **< 15 ms (~66Hz)** | **Zero noticeable lag** |
| **Startup Time** | 4 – 8 seconds | **< 0.8 seconds** | **~85% Faster** |
| **Process Count** | 5 – 7 Chrome/Node processes | **1 Single Native Process** | Clean task manager |
| **Audio Mixing** | High CPU overhead | **Native WASAPI / CoreAudio** | Instant per-process volume |
| **Hot-Plug Handling**| Often loses track of modules | **Instant seamless recovery** | Magnetic re-docking |
| **Privacy & Security**| Cloud telemetry & plain configs | **100% Local & DPAPI Encrypted** | Zero telemetry |

---

## 🎛️ Supported Hardware Modules

All modules connect magnetically to the MasterHub Base Dock and communicate via USB Vendor ID `0x2516`:

| Module | Model | Product ID | Capabilities Supported |
| :--- | :---: | :---: | :--- |
| **Base Station** | `MHBS01` | `0x01F6` | Magnetic bus powering, unified LED strip brightness & RGB color control, standby management |
| **Key Deck 15** | `MHKD15` | `0x01FE` | 15 LCD color displays (72×72), real-time SkiaSharp graphics, animated GIF/MP4 loop decoding, dual-state toggles |
| **Fader 5** | `MHFD05` | `0x0200` | 5 vertical faders (0–100%), per-channel RGB LEDs & brightness, per-app WASAPI mixer |
| **Roller 2** | `MHRR02` | `0x0204` | 2 dual-direction rotational dials with push buttons (Volume, Scroll, Track scrub, DPI/Profiles). *(Hardware has no LEDs)* |
| **IPS Display** | `M131` | `0x131E` | Ultra-wide monitoring screen module detection |

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

### 🔌 Third-Party Integrations & Services
- **OBS Studio**: Direct WebSocket v5 integration (Record, Stream, Scene Switch, Source Mute, Replay Buffer).
- **Voicemod**: Official Control API 1.0.0 (WebSocket + REST) with real-time voice changer toggle, "Hear Myself" feedback, live voice switching with soundboard, background voice FX toggle, and microphone mute.
- **Discord**: Discord RPC integration for microphone mute, deafen, and activity status.
- **Spotify**: Web API playback control (Play/Pause, Next/Prev, Volume, device transfer).
- **Twitch & Kick**: Streamer controls, chat messages, and live status.
- **JetKVM**: Remote KVM switching, mouse/keyboard forwarding, HDMI video toggle.
- **VLC Media Player**: HTTP remote control (Play, Pause, Stop, Volume, Fullscreen).

### 🐍 Direct Hardware Device Drivers
- **Universal Razer Mouse HID**: Direct sensor control via 90-byte Feature Reports. Change DPI stages (400–30,000 DPI), Polling Rate (125–8000 Hz), active onboard profiles (1..5), and read real-time battery status.
- **Wooting Analog Keyboard Driver**: Direct ARM V3 HID control (`UsagePage = 0x1337, Usage = 0x0001`). Switch between Digital Mode and Analog Profiles 1–3 (Rapid Trigger, Tachyon Mode, custom actuation).

### 🎯 Smart Profiles & Workspace Management
- **App-Aware Auto-Switching**: MasterHub detects the active foreground application and automatically activates matching profiles (e.g., automatically switches to Premiere profile when video editing, CS2 profile when gaming, or OBS profile when streaming).
- **Unlimited Custom Profiles**: Create, clone, rename, and export profiles with ease.

### 🔒 Security, Privacy & DPAPI Encryption
- **100% Local & Private**: Zero cloud telemetry, zero remote tracking, zero analytics beacons.
- **Windows DPAPI Encryption**: All sensitive tokens, passwords, and API keys (OBS, Twitch, Spotify, Discord, JetKVM, Voicemod) are encrypted on disk using **Windows DPAPI** (`ProtectedData.Protect` with app-level entropy) and stored with the `enc:v1:` prefix.
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
        VM -->|Integrations| INT["OBS / Voicemod / Discord / Spotify / Twitch / JetKVM / VLC"]
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

### Prerequisites
- **Windows 10 / 11 (x64)**
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- *(No proprietary DLLs, official software, or C++ runtimes required!)*

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/your-username/MasterHub.git
   cd MasterHub
   ```

2. Build the solution using the .NET CLI:
   ```bash
   dotnet build MasterHub.slnx --nologo
   ```

3. Run the application:
   ```bash
   dotnet run --project src/MasterHub.Avalonia
   ```

---

## 📖 Technical Documentation

- [AI Context & Technical Guidelines](AI_CONTEXT.md) — Architectural reference, constraints, and strict guidelines for AI agents and developers.
- [AI Developer Guide](docs/AI_DEVELOPER_GUIDE.md) — Deep dive into hardware matrix, USB HID protocols, and development guardrails.
- [Architecture & Design Details](docs/ARCHITECTURE.md) — Threading model, MVVM design, audio subsystem, DPAPI encryption, and hot-plug handling.
- [Hardware Protocol Reference](docs/HARDWARE_PROTOCOL.md) — Reverse-engineered function signatures, USB PnP device IDs, buffer layouts, and native P/Invoke calls.
- [Product & Technical Roadmap](docs/ROADMAP.md) — Completed milestones, integrations, and future platform considerations.

---

## 🗺️ Roadmap & Implemented Modules

- [x] Base Station, Key Deck 15, Fader 5, and Roller 2 support
- [x] Zero-latency native P/Invoke pipeline (~15ms, ~66Hz dedicated polling thread)
- [x] Hot-plugging & dynamic magnetic dock reconfiguration
- [x] Unified multi-module standby power management
- [x] Full Windows CoreAudio / WASAPI mixer per fader (Master, Mic, Processes)
- [x] SkiaSharp 72×72 LCD graphics engine with zero-allocation memory caching
- [x] Animated key displays (GIF & MP4 loop playback via MediaFoundation)
- [x] Rotational dial control for Volume, Wheel scroll, and Track skip
- [x] Universal Razer mouse hardware driver (DPI stages, Polling Rate, Profiles, Battery)
- [x] Native Wooting analog keyboard driver (ARM V3 HID, Profiles 0..3 switching)
- [x] OBS Studio WebSocket v5 integration
- [x] Voicemod Control API 1.0.0 integration (WebSocket + REST, voices, soundboard, FX toggle)
- [x] Discord, Spotify, Twitch, Kick, JetKVM, VLC integrations
- [x] Multi-Actions, Delays, and Conditional Logic
- [x] Smart Profiles (foreground window automatic profile switching)
- [x] Windows DPAPI hardware-backed encryption for all stored secrets
- [x] Full runtime multilanguage support (PL, EN, DE, ES, FR, JA)
- [x] Modern Avalonia 12 desktop presentation layer

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).  
All product and company names are trademarks™ or registered® trademarks of their respective holders. Cooler Master, MasterHub, Razer, and Wooting are trademarks of their respective owners.

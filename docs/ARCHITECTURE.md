# MasterHub .NET — Architecture & Technical Design

> **Target Framework**: .NET 10.0 (C# 13, Windows x64, Avalonia 12, SkiaSharp)  
> **Quality Standard**: 0 Errors, 0 Warnings (`dotnet build MasterHub.slnx --nologo`)  
> **Clean Code Constraint**: All `.axaml` and `.axaml.cs` files must strictly remain under **150 lines of code**.

---

## 1. System Overview

MasterHub .NET is a high-performance, native C# desktop application built to control the **Cooler Master MasterHub** modular workstation system. It replaces the resource-heavy Electron-based official software, reducing working set memory consumption from ~500 MB to **< 38 MB RAM** and cutting input-to-render latency to **< 15 ms**.

### Key Architectural Pillars
- **Native USB HID Engine**: Direct Win32 HID APIs (`SetupAPI`, `hid.dll`, `kernel32.dll`) with zero proprietary vendor DLLs or installation services.
- **Unified Hardware Deck Support**: Key Deck 15 (`0x01FE`), Fader 5 (`0x0200`), Roller 2 (`0x0204`), 3 Knobs (`0x0202`), Circular Dial Display (`0x0206`), Base Station (`0x01F6`) with auto-rotation accelerometer, and Display screen detection (`0x131E`/`0x1312`).
- **Hardware Safety Guard**: `MasterHubDeviceHandle.IsForbiddenReport` strictly filters hazardous opcodes (firmware updates, factory resets, tilt sensor decalibration `0x02 0x2F`, and SPI flash burn).
- **Stream Deck Plugin Runtime**: Built-in loopback WebSocket host compatible with Elgato Stream Deck plugins (`.streamDeckPlugin`), featuring headless Chromium/Edge execution for HTML plugins, Node.js support, DRM manifest reconstruction, and real-time SkiaSharp SVG/Canvas compositing.
- **Rich Multi-Engine Telemetry**: Native GPU/CPU monitoring via DXGI, NVML, AMD ADL2, Win32 PDH, and RTSS Shared Memory (accurate 3D gaming FPS, 0 FPS on desktop).
- **Comprehensive Integration Suite**: OBS Studio, Streamlabs Desktop, Twitch, Kick, YouTube Live, Discord, Microsoft Teams, Spotify, Voicemod, Home Assistant, JetKVM, VLC, and Windows Media (GSMTC).
- **Enterprise Security**: Windows DPAPI hardware-backed encryption (`enc:v1:`) for all stored credentials and complete user data isolation under `%APPDATA%\MasterHub`.

---

## 2. Solution Structure & Repository Map

```
d:\MasterHub\
├── MasterHub.slnx                         # Solution definition (.NET 10)
├── .gitignore                             # Production Git ignore rules
├── README.md                              # User documentation & overview
├── AI_CONTEXT.md                          # Primary AI & developer operational guide
├── docs\
│   ├── ARCHITECTURE.md                    # Technical architecture & subsystem design (this file)
│   ├── HARDWARE_PROTOCOL.md               # Reverse-engineered USB HID protocol reference
│   └── ROADMAP.md                         # Roadmap, HubDeck comparison & execution blueprints
└── src\
    ├── MasterHub.Core\                    # Domain core, hardware drivers, actions & services
    │   ├── Actions\                       # KeyInteractionEngine, Gestures, Conditionals, MultiActions
    │   ├── Audio\                         # Windows CoreAudio / WASAPI audio mixer (IAudioService)
    │   ├── Devices\                       # IMasterHubManager (Lifecycle, Polling loop, Hot-plug)
    │   ├── Integrations\                  # Third-party integrations
    │   │   ├── Discord\                   # Discord Local IPC RPC (Mute, Deafen, Camera, Go Live, PTT, Voice Channel)
    │   │   ├── HomeAssistant\             # Home Assistant REST & bidirectional WebSocket event client
    │   │   ├── JetKVM\                    # Remote KVM ATX power & status
    │   │   ├── Kick\                      # Kick chat & API (Pusher WebSocket)
    │   │   ├── Obs\                       # OBS Studio obs-websocket v5 protocol
    │   │   ├── Spotify\                   # Spotify Web API (playback, shuffle, repeat, queue, URI)
    │   │   ├── StreamDeck\                # Stream Deck plugin host, WebSocket loopback, catalog service
    │   │   ├── Streamlabs\                # Streamlabs Desktop JSON-RPC 2.0 (WebSocket 59650 / Named Pipe)
    │   │   ├── Teams\                     # Microsoft Teams desktop meeting shortcuts & focus restoration
    │   │   ├── Twitch\                    # Twitch Helix API & chat client
    │   │   ├── Vlc\                       # VLC Media Player HTTP remote control
    │   │   ├── Voicemod\                  # Official Voicemod Control API 1.0.0 (WebSocket + REST)
    │   │   └── YouTube\                   # YouTube Live API v3 & keyless public scraping
    │   ├── Localization\                  # JSON locale engine (PL, EN, DE, ES, FR, JA)
    │   ├── Media\                         # Windows Now Playing Media Session service (GSMTC)
    │   ├── Models\                        # Hardware models, device descriptors & enums
    │   ├── Monitoring\                    # System monitor (DXGI, NVML, AMD ADL2, PDH, RTSS 3D FPS)
    │   ├── Native\                        # Pure Win32 USB HID Transport (MasterHubUsbTransport)
    │   ├── Profiles\                      # ProfileService, SmartProfileService, CoolerMasterMhzImporter
    │   ├── Razer\                         # Universal 90-byte Feature Report mouse driver
    │   ├── Rendering\                     # ButtonImageRenderer, WindowsAppIconExtractor, SvgRasterizer, AnimatedKeyRenderer
    │   ├── Security\                      # Windows DPAPI encryption (SecretProtectionService)
    │   ├── SystemControl\                 # Windows input simulator, virtual desktops, startup manager
    │   └── Wooting\                       # ARM V3 HID keyboard profile controller
    │
    └── MasterHub.Avalonia\                # Presentation layer (Avalonia 12, MVVM)
        ├── Localization\                  # Dynamic Avalonia localization bindings ({loc:Loc})
        ├── Services\                      # NavigationService, TrayService, MasterHubDeviceService
        ├── Styles\                        # App styling, glassmorphism design system
        ├── ViewModels\                    # Modular presentation models
        │   ├── Canvas\                    # CanvasViewModel (modular deck coordinator & action dispatcher)
        │   ├── Inspector\                 # ActionLibraryViewModel, DeviceHeaderViewModel, InspectorViewModel
        │   ├── Pages\                     # ProfilesPageViewModel, SettingsPageViewModel, PluginCatalogViewModel, ActionStorePageViewModel, LogsPageViewModel
        │   ├── BaseStationItemViewModel.cs# Dock Base LED & brightness model
        │   ├── DialItemViewModel.cs       # 240×240 IPS Dial presentation model
        │   ├── KeyItemViewModel.cs        # 15× LCD key presentation model
        │   ├── KnobItemViewModel.cs       # 3× Knob encoder & LED ring model
        │   ├── MainViewModel.cs           # Root presentation coordinator
        │   ├── NavigationViewModel.cs     # Sidebar navigation
        │   ├── RollerItemViewModel.cs     # 2× Roller encoder model
        │   └── SliderItemViewModel.cs     # 5× Fader audio & LED model
        └── Views\                         # Modular UserControls (< 150 lines per file)
            ├── Canvas\                    # ModuleCanvasView, KeysModuleView, SlidersModuleView, RollersModuleView, DialModuleView, KnobsModuleView
            ├── Inspector\                 # InspectorPanelView, KeyActionSettingsView, KeyDisplaySettingsView, KnobInspectorView, DialInspectorView
            ├── Navigation\                # NavigationSidebarView
            ├── Pages\                     # SettingsPageView, ProfilesPageView, PluginCatalogTabView, ActionStorePageView, LogsPageView
            └── Window\                    # TitleBarView, MainWindow
```

---

## 3. Core Architectural Patterns

### 3.1. Model-View-ViewModel (MVVM)
- **View (`.axaml`)**: Declarative UI layout with zero business logic in code-behind. Every `.axaml` and `.axaml.cs` file is strictly modularized to remain **under 150 lines**.
- **ViewModel (`*ViewModel.cs`)**: Handles presentation state, exposes `ObservableCollection<T>` for dynamic lists, and encapsulates user interactions via `ICommand` / `RelayCommand`.
- **Model (`ProfileModel`, `KeyAssignmentModel`, etc.)**: Pure domain data structures without presentation dependencies.

### 3.2. Dependency Injection & Service Orientation
All core components are registered as singletons in [App.axaml.cs](../src/MasterHub.Avalonia/App.axaml.cs) using `Microsoft.Extensions.DependencyInjection`:
- Presentation depends solely on service interfaces (`IMasterHubManager`, `IAudioService`, `IObsService`, `IStreamDeckPluginHost`, `IProfileService`, etc.).
- Facilitates modular unit testing and allows running the core engine in headless scenarios.

### 3.3. SkiaSharp Caching & Dynamic Compositor (`ButtonImageRenderer`)
- The 15 physical LCD keys on the Key Deck require continuous 72×72 pixel JPEG buffers.
- `ButtonImageRenderer` pre-warms and caches all static/idle/pressed button graphics in memory (`ConcurrentDictionary<string, byte[]>`), achieving **allocation-free, sub-millisecond updates** when keys are clicked.
- **Stream Deck Key Compositor**: Dynamically layers incoming WebSocket graphics (`setImage`), multi-line text (`setTitle`) with auto-scaled typography and outline shadows, alert indicators (⚠️), success checkmarks (✅), and pressed overlays into unified 72×72 JPEG frames.
- **Panoramic Wallpaper Engine**: Slices 480×272 wallpapers into 15 individual key windows or streams single-frame panoramic reports (`0x02 0x35`).

### 3.4. Threading & Hardware Polling Loop
```
 ┌──────────────────────────────────────────────────────────────┐
 │                  Avalonia UI Thread (120 FPS)                │
 │  MainWindow.axaml <--> MainViewModel (Reactive Data Binding) │
 └──────────────┬──────────────────────────────▲────────────────┘
                │ Dispatcher.UIThread          │ Event Invocations
                ▼                              │
 ┌─────────────────────────────────────────────┴────────────────┐
 │             MasterHub-PollThread (Dedicated Thread)          │
 │  - Ultra-lightweight loop (~15ms sleep, ~66Hz refresh)       │
 │  - _hKeypad.ReadInputReport (Keypad 15 IO, non-blocking)    │
 │  - _hSlider.GetSliderValues (Fader 5 IO, Feature Rep 0x33)   │
 │  - _hRoller.ReadInputReport (Roller 2 IO, non-blocking)      │
 │  - _hKnob.ReadInputReport   (3 Knobs IO, non-blocking)       │
 │  - _hDial.ReadInputReport   (Dial rotation & press IO)       │
 └──────────────────────────────────────────────────────────────┘
                ▲
                │ Hot-Plug Triggers
 ┌──────────────┴───────────────────────────────────────────────┐
 │               HotPlugBackgroundLoop (Task Pool)              │
 │  - Runs every 2.5s or on hardware status 7 (disconnected)   │
 │  - Scans Win32 HID interfaces (SetupDiGetClassDevs)         │
 │  - Base Station accelerometer check (Report 0x35 / tag 0x2B) │
 │  - Automatically reconnects & restores magnetic modules     │
 └──────────────────────────────────────────────────────────────┘
```

---

## 4. Subsystems & Integration Modules

### 4.1. Hardware Deck Subsystem (`MasterHubManager`)
- Communicates directly via `MasterHubUsbTransport` (pure native C# Win32 USB HID layer with zero vendor DLL dependencies).
- Manages hardware lifecycle: init, device discovery, key states, fader levels, roller steps, knob rotations/clicks, dial rotations/clicks, and standby power-down.
- **Hardware Safety Filter (`IsForbiddenReport`)**: Blocks dangerous firmware flashes, factory resets, tilt sensor decalibration (`0x02 0x2F`), and SPI flash burn commands.
- **Analog Deadband & Smoothing**: Fader values are filtered with jitter-free deadbands (0..2% -> 0%, 98..100% -> 100%, and < 2% noise delta suppressed).
- **Dynamic Encoder Acceleration**: Rotary inputs from rollers, knobs, and dial calculate velocity-based multipliers (1×, 2× for <110ms, 3× for <50ms) for responsive scrolling and fine tuning.

### 4.2. Stream Deck Plugin Runtime Subsystem
- **Loopback WebSocket Server (`StreamDeckWebSocketServer`)**: Runs on port 28123 (with automatic fallback), implementing the official Elgato Stream Deck WebSocket protocol (`registerPlugin`, `willAppear`, `willDisappear`, `keyDown`, `keyUp`, `setImage`, `setTitle`, `setState`, `showAlert`, `showOk`, `openUrl`, `getSettings`, `setSettings`).
- **Tri-Modal Process Host (`StreamDeckPluginHost`)**:
  1. *Node.js Plugins*: Executed via `node.exe` with CLI registration arguments.
  2. *HTML/DOM/Canvas Plugins*: Launched via headless Microsoft Edge or Google Chrome (`--headless=new --remote-debugging-port=0 --disable-gpu`) using an internal runner `_masterhub_runner.html` executing `connectElgatoStreamDeckSocket`.
  3. *Native Binaries*: Executed directly (`*.exe`).
- **Elgato DRM Auto-Reconstruction**: Detects encrypted manifests and synthesizes valid `manifest.json` configurations dynamically from JS/HTML entry points.
- **Catalog & Marketplace Service (`PluginCatalogService`)**: Connects to the open-source plugin catalog for one-click downloading, unzipping, and installation in the **Plugin Catalog** tab.

### 4.3. Profiles, Pages & Folders (`ProfileService`)
- **Storage Isolation**: User profiles and settings persist in `%APPDATA%\MasterHub\profiles\` and `settings.json`.
- **Multi-Page & Folder Navigation**: Supports unlimited pages (`GoToPage`, `NextPage`, `PreviousPage`) and nested folder hierarchies with automatic return buttons (`GoBack`, `GoHome`, `OpenFolder`).
- **Cooler Master `.mhz` Importer (`CoolerMasterMhzImporter`)**: Imports official MasterHub backup archives (`.mhz` / `.zip`), maps action types, extracts custom icons, and converts layouts to native `ProfileModel` format.
- **Smart Profiles (`SmartProfileService`)**: Background tracking of the active foreground window via Win32 `GetForegroundWindow` with automatic profile switching (e.g. CS2, Blender, Premiere, OBS Studio).
- **Wallpaper Modes**: Per-key icons vs. full-deck panoramic mode (480×272 sliced or sent via single command `0x02 0x35`).

### 4.4. System Control, Windows Desktop & Audio
- **Windows Audio Mixer (`WindowsAudioService`)**: Windows CoreAudio / WASAPI integration controlling Master volume, Default Microphone level, and per-process audio sessions.
- **Windows Desktop & Productivity (`WindowsSystemControlService`)**:
  - Virtual Desktops: Next, Previous, Create (`Win+Ctrl+D`), Close (`Win+Ctrl+F4`).
  - Desktop Management: Task View (`Win+Tab`), Show Desktop (`Win+D`), Clipboard History (`Win+V`).
  - Text paste via clipboard, hotkey simulation, sleep, shutdown, restart, lock screen (`Win+L`), screensaver.
- **High-Res Icon Extractor (`WindowsAppIconExtractor` / `AppIconExtractorService`)**: Win32 `PrivateExtractIconsW` and `SHDefExtractIconW` extracting crisp 128×128 and 256×256 pixel icons from `.exe` and `.lnk` files.

### 4.5. Native Hardware Telemetry & Monitoring (`SystemMonitorService`)
- **Multi-Engine Hardware Monitoring**:
  - **DXGI (`dxgi.dll`)**: Enumerates physical GPUs across NVIDIA, AMD, and Intel with LUID extraction.
  - **NVML (`nvml.dll`)**: Direct NVIDIA GeForce / RTX core temperature (°C) and GPU load (%).
  - **AMD ADL2 (`atiadlxx.dll`)**: AMD Radeon & AMD Ryzen SoC/CPU core temperature (°C) and load (%).
  - **Win32 PDH (`pdh.dll`)**: CPU utility, PhysicalDisk read/write MB/s, and GPU Engine 3D usage.
  - **RTSS Shared Memory (`RTSSSharedMemoryV2`)**: Accurate real-time 3D game FPS counter with desktop non-game process filtering (0 FPS on desktop).
  - **Win32 APIs**: `GlobalMemoryStatusEx` for RAM and `GetSystemTimes` for CPU load.
  - **Local HTTP Endpoint Fallback**: Connects to LibreHardwareMonitor / OpenHardwareMonitor (`http://127.0.0.1:8085/data.json`) when available.

### 4.6. Software & Stream Integrations Suite
- **OBS Studio (`ObsService`)**: obs-websocket v5 client (Stream, Record, Virtual Cam, Replay Buffer, Scene Switch, Studio Mode Toggle, Studio Mode Transition).
- **Streamlabs Desktop (`StreamlabsService`)**: JSON-RPC 2.0 via WebSocket port 59650 and Named Pipe `\\.\pipe\slobs` for scene switching.
- **Twitch (`TwitchService`)**: Helix API & chat client (Emote-Only, Slow Mode, Sub-Only, Clear Chat, Send Message, Clips, Markers, Raids, Commercials).
- **Kick (`KickService`)**: Pusher WebSocket & Kick API (Chat modes, clear chat, send message, create clip).
- **YouTube Live (`YouTubeService`)**: Official YouTube Data API v3 + keyless public scraping fallback for live status and concurrent viewer count.
- **Discord (`DiscordService`)**: Local IPC RPC (Mute Toggle, Deafen Toggle, Camera Toggle, Screen Share / Go Live, Push-To-Talk, Join Voice Channel).
- **Microsoft Teams (`TeamsService`)**: Desktop meeting shortcuts (Mute, Camera, Raise Hand, Leave Meeting) with active window focus restoration.
- **Spotify (`SpotifyService`)**: Web API with PKCE authentication (Playback, Shuffle, Repeat, Like Track, Add to Queue, Play URI).
- **Voicemod (`VoicemodService`)**: Official Control API 1.0.0 (Voice select, voice changer toggle, mic mute, hear myself, background FX, soundboard play/stop).
- **Home Assistant (`HomeAssistantService`)**: REST API & bidirectional WebSocket client (Toggle, turn on, turn off, call service, and dial brightness control).
- **JetKVM (`JetKvmService`)**: Remote KVM ATX power control (Short press, Long press 5s, Reset, Power status).
- **VLC Media Player (`VlcService`)**: HTTP remote control (Play/Pause, Stop, Next, Previous, Fullscreen).
- **Windows Media (`NowPlayingService`)**: GSMTC media transport controls & window title tracking for Spotify, YouTube, VLC, browser players (extracts track, artist, album, and artwork).

### 4.7. Security & DPAPI Encryption (`SecretProtectionService`)
- Hardware-backed Windows DPAPI encryption (`ProtectedData.Protect` with app-level entropy).
- Encrypted secrets are tagged with the `enc:v1:` prefix.
- Profiling exports automatically purge secrets before saving.

---

## 5. Build & Quality Verification

```powershell
dotnet build MasterHub.slnx --nologo
```
- Compilation must strictly produce **0 warnings and 0 errors**.
- All `.axaml` and `.axaml.cs` files must maintain strict separation (< 150 lines).
- Never execute windowed GUI processes (`MasterHub.Avalonia.exe`) via terminal commands.

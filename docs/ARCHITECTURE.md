# MasterHub .NET Architecture & Technical Design

## 1. Overview

MasterHub .NET is an ultra-fast, native C# (.NET 10) desktop application designed for the **Cooler Master MasterHub** modular stream deck and productivity workstation. It completely replaces the resource-heavy Electron-based official software, reducing working set memory consumption from ~500 MB to **<38 MB** and slashing input latency to **sub-millisecond responsiveness**.

---

## 2. Solution Structure

The solution (`MasterHub.slnx`) is architected with strict separation of concerns into two projects:

```
d:\MasterHub\
├── MasterHub.slnx                     # Solution manifest (.NET 10)
├── .gitignore                         # Comprehensive Git ignore rules
├── README.md                          # User documentation & overview
├── AI_CONTEXT.md                      # Operational context for AI & developers
├── docs/
│   ├── ARCHITECTURE.md                # Technical design & architecture (this file)
│   ├── HARDWARE_PROTOCOL.md           # USB HID open protocol specifications
│   ├── AI_DEVELOPER_GUIDE.md          # Technical guidelines & hardware matrix for AI & developers
│   └── ROADMAP.md                     # Product & technical roadmap
└── src/
    ├── MasterHub.Core/                # Domain core, hardware drivers, actions & services
    │   ├── Actions/                   # Action engine (KeyInteractionEngine, Gestures, Conditionals)
    │   ├── Audio/                     # Windows WASAPI / CoreAudio audio mixer
    │   ├── Devices/                   # IMasterHubManager (Lifecycle, Polling loop, Hot-plug)
    │   ├── Integrations/              # External services (OBS, Voicemod, Discord, Spotify, etc.)
    │   ├── Localization/              # JSON locale engine (PL, EN, DE, ES, FR, JA)
    │   ├── Media/                     # Windows Media Session (NowPlayingService)
    │   ├── Models/                    # Hardware models & enum definitions
    │   ├── Monitoring/                # System telemetry (CPU, RAM, GPU, Net, FPS)
    │   ├── Native/                    # Pure Win32 USB HID Transport (MasterHubUsbTransport)
    │   ├── Profiles/                  # Profile & settings storage, SmartProfileService
    │   ├── Razer/                     # Direct 90-byte Feature Report mouse driver
    │   ├── Rendering/                 # SkiaSharp 72x72 caching & MediaFoundation video decoder
    │   ├── Security/                  # Windows DPAPI encryption (SecretProtectionService)
    │   ├── SystemControl/             # Win32 keystroke & mouse input simulator
    │   └── Wooting/                   # ARM V3 HID keyboard profile controller
    │
    └── MasterHub.Avalonia/            # Presentation layer (Avalonia 11, MVVM)
        ├── Common/                    # ObservableObject & RelayCommand
        ├── Converters/                # Value converters for XAML bindings
        ├── Localization/              # Avalonia dynamic localization binding
        ├── Services/                  # UI navigation, tray icon, device status services
        ├── Styles/                    # Design system (Colors, Glassmorphism, Controls)
        ├── ViewModels/
        │   ├── Canvas/                # KeyItemViewModel, SliderItemViewModel, RollerItemViewModel
        │   ├── Inspector/             # ActionLibraryViewModel, InspectorViewModel
        │   ├── Pages/                 # Profiles, Settings, Logs, Action Store ViewModels
        │   ├── MainViewModel.cs       # Root presentation coordinator
        │   └── NavigationViewModel.cs # Sidebar navigation
        └── Views/
            ├── Canvas/                # Modular hardware deck visualizers
            ├── Inspector/             # Action parameters & styling panels
            ├── Navigation/            # Sidebar navigation bar
            ├── Pages/                 # Full-page views & integration cards
            └── Window/                # MainWindow & Custom TitleBar
```

---

## 3. Core Architectural Patterns

### 3.1. Model-View-ViewModel (MVVM)
- **View (`.axaml`)**: Declarative UI layout with zero business logic in code-behind. Every `.axaml` and `.axaml.cs` file is strictly modularized to remain **under 150 lines** for maintainability.
- **ViewModel (`*ViewModel.cs`)**: Handles presentation state, exposes `ObservableCollection<T>` for dynamic lists, and encapsulates user interactions via `ICommand` / `RelayCommand`.
- **Model (`ProfileModel`, `KeyAssignmentModel`, etc.)**: Pure domain data structures without presentation dependencies.

### 3.2. Dependency Injection & Service Orientation
All core components are registered as singletons in `App.axaml.cs` using `Microsoft.Extensions.DependencyInjection`:
- Presentation depends solely on service interfaces (`IMasterHubManager`, `IAudioService`, `IObsService`, `IVoicemodService`, `IProfileService`, etc.).
- Facilitates modular unit testing and allows running the core engine in headless scenarios.

### 3.3. Cache & Flyweight Pattern (`ButtonImageRenderer`)
- The 15 physical LCD keys on the Key Deck require continuous 72×72 pixel JPEG buffers.
- Re-encoding bitmaps on every state change causes CPU spikes and GC pressure.
- `ButtonImageRenderer` pre-warms and caches all static/idle/pressed button graphics in memory (`ConcurrentDictionary<string, byte[]>`), achieving **allocation-free, sub-millisecond updates** when keys are clicked.

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
 │  - Check_Rcv_Data_CM_M4315 (Keypad 15 IO)                   │
 │  - Get_Slider_Value_CM_M0 (Fader 5 IO)                       │
 │  - Check_Rcv_Data_CM_M0 (Roller 2 IO)                        │
 └──────────────────────────────────────────────────────────────┘
                ▲
                │ Hot-Plug Triggers
 ┌──────────────┴───────────────────────────────────────────────┐
 │               HotPlugBackgroundLoop (Task Pool)              │
 │  - Runs every 2.5s or on WM_DEVICECHANGE USB notifications   │
 │  - Scans Win32_PnPEntity with `AND Present = TRUE`           │
 │  - Automatically reconnects or relocates magnetic modules    │
 └──────────────────────────────────────────────────────────────┘
```

---

## 4. Security & Secret Protection

MasterHub incorporates privacy-first data handling:

1. **Storage Isolation**:
   - Runtime configuration and profiles are saved to `%APPDATA%\MasterHub` (`profiles/` and `settings.json`).
   - Project directory remains 100% clean and free of user data.
2. **Windows DPAPI Encryption**:
   - `SecretProtectionService` wraps Windows Data Protection API (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser` and application-level entropy).
   - Sensitive fields (OBS passwords, Twitch tokens, Spotify tokens, Discord tokens, JetKVM credentials, Voicemod API keys) are stored encrypted with the prefix `enc:v1:`.
   - Stored credentials cannot be decrypted on other machines or user accounts.
3. **Safe Export**:
   - `ProfileService.ExportProfileToJson` strips all credentials before generating shareable JSON files.
4. **Git Protection**:
   - Root `.gitignore` explicitly excludes all `profiles/`, `settings.json`, `*.local.json`, `secrets.json`, `*.log`, and crash dumps.

---

## 5. Subsystems & Integration Modules

### 5.1. Hardware Deck Subsystem (`MasterHubManager`)
- Communicates directly via `MasterHubUsbTransport` (pure native C# Win32 USB HID layer with zero vendor DLL dependencies).
- Manages hardware lifecycle: init, device discovery, key states, fader levels, roller steps, and standby power-down.

### 5.2. Action & Interaction Engine (`KeyInteractionEngine`)
- Supports single-action execution, multi-action macro chains with delays, conditional branches, and gesture triggers (Tap, Double Tap, Long Press, Hold).

### 5.3. Audio Mixer Subsystem (`WindowsAudioService`)
- Integrates with Windows WASAPI / CoreAudio to control Master volume, Default Microphone level, and per-process audio sessions.
- Detects new and closed audio streams in real-time.

### 5.4. External Integrations
- **OBS Studio**: `ObsService` uses WebSocket v5 protocol (`obs-websocket-dotnet`).
- **Voicemod**: `VoicemodService` implements the official Voicemod Control API 1.0.0 via WebSocket and REST endpoints on `127.0.0.1:59196`.
- **Razer**: Direct HID Feature Reports to `0x1532` interfaces for DPI, polling rate, and profile management.
- **Wooting**: ARM V3 HID control (`UsagePage 0x1337`) for analog keyboard profile switching.
- **Discord, Spotify, Twitch, Kick, JetKVM, VLC**: Modular services with health checks and error recovery.

---

## 6. Build & Quality Verification

To verify the codebase:
```powershell
dotnet build MasterHub.slnx --nologo
```
- Compilation must produce **0 warnings and 0 errors**.
- All `.axaml` and `.axaml.cs` files must maintain strict separation (< 150 lines).

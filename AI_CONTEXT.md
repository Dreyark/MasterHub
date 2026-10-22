# MasterHub .NET — Context & Operational Guidelines for AI Agents

> **Audience**: AI Coding Assistants, LLMs, Pair-Programmers, and Autonomous Agents.  
> **Last Updated**: October 2026  
> **Target Framework**: .NET 10.0 (C# 13, Windows x64, Avalonia 12, SkiaSharp)  
> **Build Command**: `dotnet build MasterHub.slnx --nologo` (Target: 0 Errors, 0 Warnings)

---

## 1. ⚠️ Strict Guardrails & AI Agent Constraints

1. **🛑 NEVER RUN GUI EXECUTABLES**:
   - **CRITICAL**: Never run `MasterHub.Avalonia.exe` via command line or background tools. GUI processes will hang/block headless terminal sessions.
   - For all runtime code verification, use CLI build:
     ```powershell
     dotnet build MasterHub.slnx --nologo
     ```
2. **🛑 STRICT 150-LINE LIMIT FOR AXAML AND AXAML.CS**:
   - **MANDATORY**: Every `.axaml` and `.axaml.cs` file must strictly remain **under 150 lines of code**.
   - If a view grows beyond ~120 lines, proactively decompose it into specialized child `UserControl` views (e.g. in `Views/Inspector/` or `Views/Pages/`).
3. **🛑 ZERO COMPILER WARNINGS & ERRORS**:
   - Always run `dotnet build MasterHub.slnx --nologo` after any code modifications. The solution must compile with exactly 0 warnings and 0 errors.
4. **🛑 PHYSICAL HARDWARE CONSTRAINTS (HARDWARE REALITY)**:
   - **Roller Module (MHRR02 / 0x0204)**: Contains 2 rotary dials with push buttons. **It does NOT have RGB LEDs or backlighting**. Never add RGB/backlight UI controls or color pickers for the roller.
   - **Key Deck 15 (MHKD15 / 0x01FE)**: 15 LCD keys arranged in a 3×5 grid. Each key is a 72×72 pixel color display driven by JPEG streams (`0x02 0x36`). Also supports panoramic 480×272 stream (`0x02 0x35`). Minimum safe backlight is 1 (0 locks MCU).
   - **Slider Module (MHFD05 / 0x0200)**: 5 faders (values 0..100) with independent RGB LEDs per fader channel and adjustable brightness. Features analog deadband filtering (`<= 2` -> 0, `>= 98` -> 100).
   - **Base Station (MHBS01 / 0x01F6)**: Magnetic dock base with an RGB LED lightbar strip and built-in orientation accelerometer (Feature Report `0x35`: 1 = Horizontal flat, 0 = Vertical upright stand).
   - **3 Knobs Module (MHKB03 / 0x0202)**: 3 endless rotary encoders with center push buttons, dynamic velocity acceleration (1×/2×/3×), and addressable RGB LED rings (10 hardware lighting modes).
   - **Circular Dial Module (MHDD01 / 0x0206)**: 240×240 IPS circular display (`0x02 0x45`), center click switch, software mode activation (`0x03 0x49`), outer rotary dial with velocity acceleration. Minimum safe backlight is 1.
   - **Display Screen Module (0x131E / 0x1312)**: Ultra-wide monitoring screen detection.
   - **Hardware Safety Guard**: `MasterHubDeviceHandle.IsForbiddenReport` blocks dangerous commands like firmware flash, recalibrate direction (`0x02 0x2F`), and SPI flash picture writes (`0x02 0x38`, `0x02 0x47`).
5. **🛑 SECURITY, PASSWORDS & REPO SANITIZATION**:
   - Never write profiles, tokens, passwords, or crash logs to the repository directory.
   - All runtime user configuration is stored in `%APPDATA%\MasterHub\`.
   - All external service credentials (OBS, Twitch, Spotify, Discord, JetKVM, Voicemod, Home Assistant, Streamlabs, YouTube) must be protected with `ISecretProtectionService` (Windows DPAPI with `enc:v1:` prefix).
   - Profile export (`IProfileService.ExportProfileToJson`) strictly strips credentials.
6. **🛑 USER COMMUNICATION LANGUAGE**:
   - The user communicates in Polish. All final responses, explanations, and summaries to the user must be written in Polish.
7. **🛑 CODE INTEGRITY & BACKWARDS COMPATIBILITY**:
   - Preserve existing comments, docstrings, profile fields, and localization keys. Do not break JSON serialization of `ProfileModel`.

---

## 2. Solution Structure & Repository Map

```
d:\MasterHub\
├── MasterHub.slnx                         # Solution File (.NET 10)
├── .gitignore                             # Production Git Ignore Configuration
├── AI_CONTEXT.md                          # Primary context & operational rules for AI (this file)
├── README.md                              # User guide & project overview
├── docs\
│   ├── ARCHITECTURE.md                    # Technical architecture & design patterns
│   ├── HARDWARE_PROTOCOL.md               # Reverse-engineered USB HID open protocol specification
│   └── ROADMAP.md                         # Roadmap, HubDeck comparison & execution blueprints
└── src\
    ├── MasterHub.Core\                    # Pure domain logic, hardware drivers, SkiaSharp rendering
    │   ├── Actions\                       # KeyInteractionEngine, Gestures, Conditionals, MultiActions
    │   ├── Audio\                         # IAudioService, WindowsAudioService (WASAPI / CSCore)
    │   ├── Devices\                       # IMasterHubManager, MasterHubManager (lifecycle, hotplug, polling)
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
    │   ├── Localization\                  # ILocalizationService, LocalizationService, Locales (*.json)
    │   ├── Media\                         # INowPlayingService, NowPlayingService, GSMTC track info & artwork
    │   ├── Models\                        # DeviceInfo, KeyStateMode, ModuleType, DeckOrientation
    │   ├── Monitoring\                    # ISystemMonitorService (DXGI, NVML, AMD ADL2, PDH, RTSS 3D FPS)
    │   ├── Native\                        # Pure Win32 USB HID Transport (MasterHubUsbTransport, MasterHubDeviceHandle)
    │   ├── Profiles\                      # ProfileService, ProfileModel, SmartProfileService, CoolerMasterMhzImporter
    │   ├── Razer\                         # IRazerService, RazerService (Universal 90-byte HID mouse driver)
    │   ├── Rendering\                     # ButtonImageRenderer, WindowsAppIconExtractor, SvgRasterizer, AnimatedKeyRenderer
    │   ├── Security\                      # ISecretProtectionService, SecretProtectionService (Windows DPAPI)
    │   ├── SystemControl\                 # Windows input simulator, virtual desktops, startup manager
    │   └── Wooting\                       # IWootingService, WootingService (HID UsagePage 0x1337/0xFF55 driver)
    │
    └── MasterHub.Avalonia\                # Avalonia Desktop Presentation (.NET 10, Avalonia 12)
        ├── Localization\                  # Avalonia dynamic localization binding ({loc:Loc})
        ├── Services\                      # NavigationService, TrayService, MasterHubDeviceService
        ├── Styles\                        # App styling & design system
        ├── ViewModels\                    # Modular ViewModels
        │   ├── Canvas\                    # CanvasViewModel (modular deck coordinator & action dispatcher)
        │   ├── Inspector\                 # ActionLibraryViewModel, DeviceHeaderViewModel, InspectorViewModel
        │   ├── Pages\                     # ProfilesPageViewModel, SettingsPageViewModel, PluginCatalogViewModel, ActionStorePageViewModel, LogsPageViewModel
        │   ├── BaseStationItemViewModel.cs# Dock Base LED & brightness model
        │   ├── DialItemViewModel.cs       # 240×240 IPS Dial presentation model
        │   ├── KeyItemViewModel.cs        # 15× Keypad LCD presentation model
        │   ├── KnobItemViewModel.cs       # 3× Knob encoder & LED ring model
        │   ├── MainViewModel.cs           # Main coordinator ViewModel
        │   ├── NavigationViewModel.cs     # Navigation sidebar ViewModel
        │   ├── RollerItemViewModel.cs     # 2× Encoder dial model
        │   └── SliderItemViewModel.cs     # 5× Fader audio target & LED model
        └── Views\                         # Modular UserControls (< 150 lines per file)
            ├── Canvas\                    # ModuleCanvasView, KeysModuleView, SlidersModuleView, RollersModuleView, DialModuleView, KnobsModuleView
            ├── Inspector\                 # InspectorPanelView, KeyActionSettingsView, KeyDisplaySettingsView, KnobInspectorView, DialInspectorView
            ├── Navigation\                # NavigationSidebarView
            ├── Pages\                     # SettingsPageView, ProfilesPageView, PluginCatalogTabView, ActionStorePageView, LogsPageView
            └── Window\                    # TitleBarView, MainWindow
```

---

## 3. Hardware Protocol Quick Reference

- **Open Source USB HID Transport**: `MasterHubUsbTransport.cs` in `MasterHub.Core/Native/`.
  - Zero proprietary DLLs, zero installation dependencies, standalone pure C#.
  - Handles Base Station (`0x01F6`), Key Deck 15 (`0x01FE`), Fader 5 (`0x0200`), Roller 2 (`0x0204`), 3 Knobs (`0x0202`), Dial Display (`0x0206`), Display (`0x131E`/`0x1312`).
- **Vendor ID**: `0x2516` (Cooler Master).
- **Key Display**: 72×72 pixel JPEG buffers sent via 320-byte chunked Output Reports (`0x02 0x36`, 312 B payload).
- **Panoramic Display**: Full 480×272 pixel single-frame panoramic wallpaper delivery via Output Reports (`0x02 0x35`, 312 B payload per chunk, ~11 ms).
- **Fader Value**: Raw slider positions 0..100 read via Input Reports or Feature Report `0x33`, with deadband jitter filtering (`<= 2` -> 0, `>= 98` -> 100).
- **LED Control**: 64-byte Output Reports (`0x02 0x2D` for Color, `0x02 0x2E` for Brightness, `0x02 0x2C` for Mode, `0x02 0x30` for Speed). DeviceType 0 = Base, 1 = Sliders, 2 = 3 Knobs.
- **Knob LED Rings**: Mode `0x02 0x2C` with 10 lighting modes (Disable, Static, Breathing, Strobe, Spring, Cycling, Random, Wave, Water, Rainbow), speed `0x02 0x30`, and velocity acceleration (1×, 2× <110ms, 3× <50ms).
- **Dial Display**: Feature Report `0x03 0x49` software activation, Feature Report `0x03 0x44` brightness (1..100), Feature Report `0x03 0x46` fill color, Output Report `0x02 0x45` 240×240 IPS JPEG streaming (320 B chunks).
- **Base Accelerometer**: Feature Report `0x35` (1 = Horizontal, 0 = Vertical).
- **Razer Mouse HID**: Vendor ID `0x1532`, 90-byte Feature Reports (DPI 400..30000, Polling 125..8000 Hz, Profiles 1..5, Battery).
- **Wooting Keyboard HID**: Vendor ID `0x31E3`, UsagePage `0x1337`/`0xFF55` (Digital / Analog 0..3 profiles).
- **Stream Deck Host**: Loopback WebSocket on port 28123 with Node.js, Headless Edge/Chrome runner (`--headless=new`), and SkiaSharp dynamic compositor.
- **System Monitoring**: Direct Win32 / hardware APIs (DXGI for GPUs, NVML for NVIDIA, AMD ADL2 for Radeon & Ryzen SoC/CPU, PDH for 3D engine and disk, RTSS for gaming FPS).

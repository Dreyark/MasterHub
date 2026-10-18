# MasterHub .NET - Context & Operational Guidelines for AI Agents

> **Audience**: AI Coding Assistants, LLMs, Pair-Programmers, and Autonomous Agents.  
> **Last Updated**: October 2026  
> **Target Framework**: .NET 10.0 (C# 13, Windows x64, Avalonia 11, SkiaSharp)  
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
   - **Roller Module (M0062 / MHRR02)**: Contains 2 rotary dials with push buttons. **It does NOT have RGB LEDs or backlighting**. Never add RGB/backlight UI controls or color pickers for the roller.
   - **Key Deck 15 (M0061 / MHKD15)**: 15 LCD keys arranged in a 3×5 grid. Each key is a 72×72 pixel color display driven by JPEG streams.
   - **Slider Module (M0063 / MHFD05)**: 5 faders (values 0..100) with independent RGB LEDs per fader channel and adjustable brightness.
   - **Base Station (M0064 / MHBS01)**: Magnetic dock base with an RGB LED lightbar strip.
5. **🛑 SECURITY, PASSWORDS & REPO SANITIZATION**:
   - Never write profiles, tokens, passwords, or crash logs to the repository directory.
   - All runtime user configuration is stored in `%APPDATA%\MasterHub\`.
   - All external service credentials (OBS, Twitch, Spotify, Discord, JetKVM, Voicemod) must be protected with `ISecretProtectionService` (Windows DPAPI with `enc:v1:` prefix).
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
│   ├── AI_DEVELOPER_GUIDE.md              # Technical guidelines & hardware matrix for AI & developers
│   ├── HARDWARE_PROTOCOL.md               # Reverse-engineered USB HID open protocol specification
│   └── ROADMAP.md                         # Product & technical roadmap
└── src\
    ├── MasterHub.Core\                    # Pure domain logic, hardware drivers, SkiaSharp rendering
    │   ├── Actions\                       # KeyInteractionEngine, Gestures, Conditionals, MultiActions
    │   ├── Audio\                         # IAudioService, WindowsAudioService (WASAPI / NAudio)
    │   ├── Devices\                       # IMasterHubManager, MasterHubManager (lifecycle, hotplug, polling)
    │   ├── Integrations\                  # OBS, Voicemod, Discord, Spotify, Twitch, Kick, JetKVM, VLC, YouTube, Teams
    │   ├── Localization\                  # ILocalizationService, LocalizationService, Locales (*.json)
    │   ├── Media\                         # INowPlayingService, NowPlayingService, NowPlayingInfo
    │   ├── Models\                        # DeviceInfo, KeyStateMode, ModuleType
    │   ├── Monitoring\                    # ISystemMonitorService, SystemMonitorService (CPU/RAM/GPU/Disk/Net/FPS)
    │   ├── Native\                        # Pure Win32 USB HID Transport (MasterHubUsbTransport, MasterHubDeviceHandle)
    │   ├── Profiles\                      # ProfileService, ProfileModel, SmartProfileService, Settings models
    │   ├── Razer\                         # IRazerService, RazerService (Universal 90-byte HID mouse driver)
    │   ├── Rendering\                     # KeyRenderer, AnimatedKeyRenderer, ButtonImageRenderer, MediaFoundationVideoDecoder
    │   ├── Security\                      # ISecretProtectionService, SecretProtectionService (Windows DPAPI)
    │   ├── SystemControl\                 # ISystemControlService, WindowsSystemControlService
    │   └── Wooting\                       # IWootingService, WootingService (HID UsagePage 0x1337 analog keyboard driver)
    │
    └── MasterHub.Avalonia\                # Avalonia Cross-Platform Desktop Presentation (.NET 10)
        ├── Common\                        # ObservableObject, RelayCommand
        ├── Converters\                    # XAML Value Converters
        ├── Localization\                  # Avalonia dynamic localization binding
        ├── Services\                      # NavigationService, TrayService, MasterHubDeviceService
        ├── Styles\                        # App styling & design system
        ├── ViewModels\                    # Modular ViewModels (<150 lines per file)
        │   ├── Canvas\                    # CanvasViewModel, KeyItemViewModel, SliderItemViewModel, RollerItemViewModel
        │   ├── Inspector\                 # ActionLibraryViewModel, DeviceHeaderViewModel, InspectorViewModel
        │   ├── Pages\                     # ProfilesPageViewModel, SettingsPageViewModel, LogsPageViewModel, etc.
        │   ├── MainViewModel.cs           # Main coordinator ViewModel
        │   └── NavigationViewModel.cs     # Navigation sidebar ViewModel
        └── Views\                         # Modular UserControls (<150 lines per file)
            ├── Canvas\                    # ModuleCanvasView, KeysModuleView, SlidersModuleView, RollersModuleView
            ├── Inspector\                 # InspectorPanelView, KeyInspectorView, SliderInspectorView, RollerInspectorView, etc.
            ├── Navigation\                # NavigationSidebarView
            ├── Pages\                     # SettingsPageView, ProfilesPageView, LogsPageView, ActionStorePageView, etc.
            └── Window\                    # TitleBarView, MainWindow
```

---

## 3. Hardware Protocol Quick Reference

- **Open Source USB HID Transport**: `MasterHubUsbTransport.cs` in `MasterHub.Core/Native/`.
  - Zero proprietary DLLs, zero installation dependencies, standalone pure C#.
  - Handles Base Station (`0x01F6`), Key Deck 15 (`0x01FE`), Fader 5 (`0x0200`), Roller 2 (`0x0204`).
- **Vendor ID**: `0x2516` (Cooler Master).
- **Key Display**: 72×72 pixel JPEG buffers sent via 320-byte chunked Output Reports.
- **Fader Value**: Raw slider positions 0..100 read via Input Reports or Feature Report `0x33`.
- **LED Control**: 64-byte Output Reports (`0x2D` for Color, `0x2E` for Brightness, `0x2C` for Mode). DeviceType 0 = Base, 1 = Sliders.
- **Razer Mouse HID**: Vendor ID `0x1532`, 90-byte Feature Reports.
- **Wooting Keyboard HID**: Vendor ID `0x31E3`, UsagePage `0x1337`, Usage `0x0001`.
- **Voicemod Control API**: WebSocket + REST on `127.0.0.1:59196`, Client API version `1.0.0`.

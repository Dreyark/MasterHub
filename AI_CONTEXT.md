# MasterHub .NET — Context & Operational Guidelines for AI Agents

> **Audience**: AI Coding Assistants, LLMs, Pair-Programmers, and Autonomous Agents.  
> **Last Updated**: November 2026  
> **Target Framework**: .NET 10.0 (C# 13, Windows x64, Avalonia 12, SkiaSharp)  
> **Build Command**: `dotnet build MasterHub.slnx --nologo` (Target: 0 Errors, 0 Warnings)  
> **Test Command**: `dotnet test MasterHub.slnx --nologo` (all tests must pass; no hardware, no GUI window)

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
├── CHANGELOG.md, PRIVACY.md, SECURITY.md, NOTICE.md, CONTRIBUTING.md   # T-33: PRIVACY.md must list every outbound host – update it when adding one; .githubworkflowsci.yml builds + tests
├── docs\
│   ├── ARCHITECTURE.md                    # Technical architecture & design patterns
│   ├── HARDWARE_PROTOCOL.md               # Reverse-engineered USB HID open protocol specification
│   └── ROADMAP.md                         # Roadmap, feature backlog & execution blueprints
├── tests\
│   ├── MasterHub.Core.Tests\              # xUnit: profiles/security, safety guard, key engine, plugin install, source/i18n/style integrity
│   └── MasterHub.Avalonia.Tests\          # xUnit v3 + Avalonia.Headless: renders every view to PNG, hover/gallery checks
└── src\
    ├── MasterHub.Cli\                     # masterhub-cli.exe (T-24): console program (also `--claude-hook` T-25 and `--mcp` T-26: Mcp\McpServer + DesignGuide; the assistant's limits are enforced in Core\Api\AssistantGuard), CliRunner (status / profiles / profile / key / lights / doctor); talks only to the running app's local API
    ├── MasterHub.Core\                    # Pure domain logic, hardware drivers, SkiaSharp rendering
    │   ├── Actions\                       # KeyInteractionEngine, Gestures, Conditionals, MultiActions
    │   ├── Updates\                       # T-32: UpdateService (GitHub Releases, off by default, notify only), UpdateSettings (AppSettings.Updates)
    │   ├── Status\                        # T-25: StatusBoard (6 shared states, in memory), AgentTracker (Claude Code sessions, only the project folder name), StatusRequests (validation of POST /api/state and /api/agent)
    │   ├── Api\                           # LocalApiServer (loopback HTTP + bearer token), ProfileValidator (T-04); Client\ = LocalApiClient, ApiConnection (reads api-port/api-token), ProfileResolver, Doctor – shared by the CLI (T-24) and later the MCP server
    │   ├── Apps\                          # IInstalledAppsService (Start menu, Store, Steam, Epic, GOG), RunningApps (T-36)
    │   ├── Audio\                         # IAudioService, WindowsAudioService (WASAPI / CSCore); Soundboard.cs + NAudioSoundOutput.cs = ISoundboardService (play modes, device|file voices, 30 s idle close, Media Foundation decoding, T-14)
    │   ├── Dialogs\                       # IConfirmDialogService: awaitable in-app confirmations (T-03)
    │   ├── Editing\                       # KeyEditing (move / swap / paste look), EditHistory (undo / redo, T-06)
    │   ├── LiveValues\                    # ILiveValueService, JSON path, ICS schedule, Open-Meteo, token / command stores (T-11)
    │   ├── Looks\                         # KeyLookEvaluator ("When" rules as layers, hysteresis), LabelTokens, LookDemand (T-12)
    │   ├── Timers\                        # DeckTimer (pure state machine, injected clock), ITimerService keyed by profile + page + key (T-13)
    │   ├── Devices\                       # IMasterHubManager, MasterHubManager (lifecycle, hotplug, polling), SentKeyImages (T-30: an unchanged key picture is not re-sent; cleared on connect/standby/rotation/panoramic), DeckLinkStatus (T-34 #13: "Connecting…" grace after start)
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
    │   ├── Profiles\                      # ProfileService, ProfileModel, SmartProfileService + ForegroundWatcher, CoolerMasterMhzImporter
    │   │   ├── Draft\                     # DraftInfo / DraftEnvelope, TourAdvisor (focus suggestion, template mapping) – drafts live in ProfileService.Draft.cs (T-08)
    │   │   ├── Auto\                      # Assistant (T-37, opt-in): AssistSettings(+Store), AppUsageService (time in front), PressCounter, AutoProfileService (switched-off profiles, offered once), NudgeService (3 rules), ModuleSketch
    │   │   └── Backup\                    # BackupService (snapshots/restore), ProfileMigrator (schema version, T-02)
    │   ├── Razer\                         # IRazerService, RazerService (Universal 90-byte HID mouse driver)
    │   ├── Rendering\                     # ButtonImageRenderer (Feedback: failure corner T-07, hold bar T-03), KeyFrame (key shape: picture = full LCD, colour/text = rounded card; glyph fallback for CJK in Text.cs), WindowsAppIconExtractor, SvgRasterizer, AnimatedKeyRenderer; Icons\ = IconLibrary (Lucide, gz resource, T-21a), IconFiles (tinted SVG → media store)
    │   ├── Notices\                       # NoticeCenter: banners in the window (T-34 #8)
    │   ├── Diagnostics\                   # AppLog (ring buffer for the Logs page), OutboundCallCounter (calls per host, T-34 #5)
    │   ├── Profiles\Check\                # ProfileCheck + SystemProfileCheckEnvironment: what a profile points at that this PC lacks (T-34 #9)
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
            ├── Controls\                  # PageHeader (shared page header: icon chip, title = nav label, subtitle, actions)
            ├── Navigation\                # NavigationSidebarView (labels + groups, collapsible; state in AppSettings.SidebarCollapsed)
            ├── Pages\                     # SettingsPageView, ProfilesPageView, PluginCatalogTabView, ActionStorePageView, LogsPageView
            └── Window\                    # TitleBarView, MainWindow
```

---

### UI styling rules (enforced by tests in `tests/MasterHub.Core.Tests/SourceIntegrityTests.cs`)
- No loose hex colours in views: use tokens from `Styles/Colors.axaml` / `Brushes.axaml` (`AccentSuccessBrush`, `InsetBgBrush`, `OnAccentBrush` …). Brand colours of third-party logos are the only exception.
- Every `Button`/`ToggleButton` needs a style class: `actionBtn` (secondary), `accentBtn` (primary), `pillBtn`, `iconBtn`, `navItem` (sidebar), `ghostBtn`, `glyphBtn`, `swatchBtn` (colour swatch). A class used in AXAML must be defined in `Styles/`.
- Icons: **no emoji in views** - use vector `PathIcon` from `Styles/Icons.axaml` (one filled Material family). Action/category icons come from `Views/Controls/IconConverters` (`ForAction`, `ByKey`, `ByAction`); put them in `Border.iconTile`. `{loc:Loc}` strips leading emoji from texts automatically (locale files keep them: the LCD renderer uses them). New `DeckActionType` must resolve to an existing icon (`IconCoverageTests`).
- Brand logos in `Styles/Icons.axaml` (Razer, OBS, Home Assistant, YouTube, Elgato, Teams, Streamlabs; Discord/Twitch/Kick/Spotify/VLC were added earlier) come from Simple Icons (CC0). To add one: `https://cdn.jsdelivr.net/npm/simple-icons@13.21.0/icons/<slug>.svg`, copy the single `<path d>` into a `StreamGeometry` `Icon<Name>`. Trademarks belong to their owners (used only to label compatible integrations).
- Layout rhythm: 16 px gap between sidebar, page panels and window edges (`MainWindow.axaml`); top edges aligned under the 52 px title bar.
- Full-window preview without hardware: `tests/MasterHub.Avalonia.Tests/StudioShotTests.cs` (real `MainWindow` on stub services -> PNG).
- App shell (`Styles/Shell.axaml`): full-height sidebar + title bar with breadcrumb (`MasterHub › page`) on the window frame (`MainBgBrush`); each page sits in a `Border.pagePanel` (radius 20) and starts with `controls:PageHeader` whose title uses the same `NavLabel_*` key as the sidebar. Hints go in `Border.callout` (not tiny `dim` text); filters use `Border.segmented` + `pillBtn.active`. `TextMuted` is for readable secondary text (>= 4.5:1 on cards); `TextDim` only for non-essential hints.
- Button states (`:pointerover`, `:pressed`, `:disabled`, active) must target `/template/ ContentPresenter#PART_ContentPresenter`, otherwise Fluent overrides them.
- User-visible text goes through `{loc:Loc Key}` / `GetString` and must exist in all 6 locale files (UTF-8, no mojibake). Windows must not reference assets that do not exist (`/Assets` has none).
- To *see* a view without running the app: `dotnet test tests/MasterHub.Avalonia.Tests` renders views headless (Skia) to PNG in `%TEMP%\mh_shots` (override with `MH_SHOTS`). This does not start the GUI executable.

### Large classes are split into `partial` files
`ButtonImageRenderer` (Core/Rendering), `CanvasViewModel` (ViewModels/Canvas) and `KeyItemViewModel` (ViewModels) are `partial` classes
split by concern: `Type.cs` holds fields, constructor and shared helpers; `Type.<Concern>.cs` holds one feature area
(e.g. `ButtonImageRenderer.Obs.cs`, `CanvasViewModel.KeyActions.Streaming.cs`, `KeyItemViewModel.Typography.cs`).
When adding code, put it into the matching part (or add a new part) instead of growing one file; static fields / caches stay in the main file
(initialisation order across partial files is undefined). Key actions are dispatched via `TryExecute<Category>Action` handlers in `CanvasViewModel.KeyActions*.cs`.

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

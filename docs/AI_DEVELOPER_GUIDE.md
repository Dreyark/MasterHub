# MasterHub .NET — AI & Developer Architecture Guide

> **Audience**: AI Coding Assistants, LLMs, Pair-Programmers, and Software Engineers.  
> **Target Framework**: .NET 10.0 (C# 13, Windows x64, SkiaSharp, Avalonia 11).  
> **Quality Standard**: 0 Errors, 0 Warnings (`dotnet build MasterHub.slnx --nologo`).  
> **Clean Code Constraint**: All `.axaml` and `.axaml.cs` files must strictly remain under **150 lines of code**.

---

## 1. Project Overview & Architecture

MasterHub .NET is a high-performance, native C# (.NET 10) desktop application designed for the **Cooler Master MasterHub** modular stream deck and productivity workstation.
It completely replaces the resource-heavy Electron-based official software (~500 MB RAM, 80–120 ms latency), delivering:
- **RAM Footprint**: < 38 MB at idle (~93% reduction).
- **Input Latency**: < 15 ms (hardware polling loop running at ~66 Hz on a dedicated background thread).
- **LCD Rendering Engine**: O(1) allocation-free memory overhead via pre-cached 72×72 JPEG image buffers powered by SkiaSharp.
- **Enterprise Security**: Windows DPAPI hardware-backed encryption (`enc:v1:`) for all stored credentials, with full data isolation in `%APPDATA%\MasterHub`.

### Solution Structure (`MasterHub.slnx`)
1. **`MasterHub.Core`** (`net10.0-windows`): Pure domain logic, native open-source Win32 USB HID transport (`MasterHubUsbTransport`), native HID drivers (Razer mouse, Wooting keyboard), system services (WASAPI audio mixer, Windows media sessions, hardware monitoring), third-party integrations (OBS Studio v5, Voicemod 1.0, Discord, Spotify, Twitch, Kick, JetKVM, VLC), action & gesture engine, profile persistence, and SkiaSharp rendering. Zero GUI dependencies.
2. **`MasterHub.Avalonia`** (`net10.0-windows`): Modern, cross-platform vector UI built with Avalonia 11 and MVVM architecture. Completely decoupled into modular components: Canvas, Inspector, Navigation, and Pages.

---

## 2. ⚠️ Critical Guardrails & Development Constraints

Every developer and AI agent working on this repository **must strictly observe** the following rules:

1. **NEVER LAUNCH GUI PROCESSES IN CLI**:
   - **CRITICAL**: Never run `MasterHub.Avalonia.exe` in terminal or headless environments. Windowed processes will block terminal sessions and freeze execution.
   - Run verification exclusively via CLI:
     ```powershell
     dotnet build MasterHub.slnx --nologo
     ```
2. **STRICT 150-LINE LIMIT PER AXAML & AXAML.CS FILE**:
   - Every view file (`.axaml`) and its code-behind (`.axaml.cs`) **must strictly stay under 150 lines of code**.
   - When adding controls, cards, or property sections, decompose them into dedicated child `UserControl` views (e.g. under `Views/Inspector/` or `Views/Pages/`) and compose them into parent layouts.
3. **ZERO COMPILER WARNINGS & ERRORS**:
   - Every build must compile cleanly with `0 Warning(s)` and `0 Error(s)`.
4. **PHYSICAL HARDWARE REALITY (ROLLER MODULE M0062)**:
   - The dual rotary dial module (**M0062 / MHRR02**) **DOES NOT HAVE PHYSICAL LEDS OR BACKLIGHTING**.
   - **NEVER** add color pickers, brightness sliders, or RGB toggle buttons for the roller in the user interface.
   - RGB backlighting is physically present only on: **Faders (M0063)** (per-channel) and **Base Station (M0064)** (integrated lightbar).
   - The **Key Deck 15 (M0061)** LCD screens support display matrix brightness control (`KeypadBrightness`).
5. **SECURITY, PRIVACY & REPO PURITY**:
   - Never write user profiles, passwords, API tokens, or crash logs to the repository directory.
   - All runtime configuration files are saved to `%APPDATA%\MasterHub\`.
   - All credentials and sensitive tokens must be encrypted via `ISecretProtectionService` (Windows DPAPI with `enc:v1:` prefix).
   - Profile export (`IProfileService.ExportProfileToJson`) strictly sanitizes all passwords and tokens before export.
6. **BACKWARD COMPATIBILITY**:
   - Never remove existing properties from `ProfileModel`, `KeyAssignmentModel`, `SliderAssignmentModel`, or `RollerAssignmentModel`. User profiles persist in JSON format and require seamless backward compatibility.

---

## 3. Hardware Matrix & Communication Protocols

All Cooler Master MasterHub modular devices share USB Vendor ID: **`0x2516`**.

| Module | Model Code | Product ID | USB HID Driver | Input Mechanism | Illumination / Display |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Base Station** | `MHBS01` | `0x01F6` | `MasterHubUsbTransport` | Magnetic USB dock bus | RGB LED lightbar strip (Color + Brightness) |
| **Key Deck 15** | `MHKD15` | `0x01FE` | `MasterHubUsbTransport` | 15 physical keys (3×5 grid) | 15 LCD screens 72×72 px (JPEG stream) + Matrix brightness |
| **Fader 5** | `MHFD05` | `0x0200` | `MasterHubUsbTransport` | 5 linear sliders (values 0..100) | 5 RGB LED zones + Global brightness |
| **Roller 2** | `MHRR02` | `0x0204` | `MasterHubUsbTransport` | 2 rotary encoders (steps + direction) | **NO HARDWARE LEDS** |

### 3.1. Pure C# USB HID Transport (`MasterHubUsbTransport`)
All hardware communication is implemented in pure C# using Win32 HID APIs (`kernel32.dll`, `hid.dll`, `setupapi.dll`), completely decoupled from proprietary vendor DLLs:
- `DisplayPictureOnButton(buttonId, jpegBuf, bufLen)`: 320-byte chunked Output Report transfer of 72×72 JPEG image streams directly to individual LCD buttons.
- `ReadInputReport(buf, timeout)`: Reads asynchronous HID Input Reports (`buf[2..16]` for 15-keypad press state: 1 = pressed, 0 = released; `buf[2..6]` for 5-slider position 0..100; `buf[2..3]` for 2-roller rotation direction).
- `GetSliderValues(buf)`: Feature Report `0x33` synchronously reading positions of the 5 faders.
- `AdjustLedBrightness(deviceType, ledIndex, brightness)`: 64-byte Output Report `0x2E`.
  - `deviceType`: `0` = Base Station, `1` = Faders.
  - `ledIndex`: `0` for Base Station, `0xFF` for all faders, or 0..4 for individual fader channel.
- `AdjustLedColor(deviceType, ledIndex, r, g, b, saveFw)`: 64-byte Output Report `0x2D` setting RGB color.
- **Hardware Fault & Hot-Plug Handling**: Return code `7` indicates physical module detachment from the magnetic bus. The runtime safely disposes the `MasterHubDeviceHandle`, marks the device offline, and initiates background PnP re-discovery.

### 3.2. Razer Mouse Driver (Direct HID Feature Reports)
- **Vendor ID**: `0x1532`.
- **Communication**: Direct `HidD_SetFeature` / `HidD_GetFeature` reports (buffer length >= 90 bytes).
- Supports DPI stages (400–30,000 DPI), polling rate switching (125–8000 Hz), onboard profile selection (1..5), and real-time battery charge telemetry.

### 3.3. Wooting Keyboard Driver (ARM V3 HID)
- **Communication**: Direct HID control interface (`UsagePage = 0x1337, Usage = 0x0001`).
- On-the-fly switching between Digital Mode (Profile 0) and Analog Profiles 1..3 (Rapid Trigger, Tachyon Mode, custom actuation curves).

---

## 4. Integration Ecosystem

1. **Voicemod (Official Control API 1.0.0)**:
   - WebSocket + REST endpoints on `127.0.0.1:59196`.
   - Voice changer toggle, Soundboard playback, Background Voice FX toggle, "Hear Myself" audio monitoring toggle, and microphone mute.
2. **OBS Studio**:
   - obs-websocket v5 protocol (`ObsService`).
   - Scene switching, streaming/recording toggle, audio source muting, and replay buffer triggers.
3. **Discord**:
   - RPC integration (`DiscordService`) for microphone mute and deafen toggles.
4. **Spotify Web API**:
   - PKCE authentication, playback control, volume adjustment, and current track metadata.
5. **Twitch & Kick**:
   - Chat message delivery, live stream telemetry, and moderation shortcuts.
6. **JetKVM & VLC**:
   - Remote KVM video/input forwarding and VLC media player HTTP playback control.

---

## 5. Action Engine & Rendering Subsystems

- **Gestures (`KeyInteractionEngine`)**: Real-time recognition of Single Tap, Double Tap, Long Press, and Hold interactions.
- **Conditional Actions**: Dynamic execution branching based on application states (e.g., OBS streaming state, Spotify active playback, system conditions).
- **Multi-Actions**: Chained macro sequences with configurable delays (`MultiActionStepModel`).
- **SkiaSharp Caching (`ButtonImageRenderer`)**:
  - 72×72 pixel high-contrast graphics generation with in-memory caching (`ConcurrentDictionary<string, byte[]>`).
  - Dual-state icons (Default / Active state toggling).
  - Hardware-accelerated GIF and MP4 video loop decoding via `MediaFoundationVideoDecoder`.
- **Smart Profiles (`SmartProfileService`)**:
  - Background tracking of the active foreground window via Win32 `GetForegroundWindow`.
  - Automatically switches profiles to match the active application (e.g. CS2, Blender, Premiere, OBS Studio).

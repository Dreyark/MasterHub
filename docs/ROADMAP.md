# MasterHub .NET — Product & Technical Roadmap

## Completed Milestones

### 1. Hardware Architecture & Zero-Latency Engine
- [x] Full support for **Base Station (MHBS01)**, **Key Deck 15 (MHKD15)**, **Fader 5 (MHFD05)**, and **Roller 2 (MHRR02)**.
- [x] Dedicated high-priority hardware polling thread (`MasterHub-PollThread`, ~66 Hz / ~15 ms cycle).
- [x] Allocation-free SkiaSharp 72×72 LCD graphics rendering pipeline with `ConcurrentDictionary` bitmap and JPEG caching.
- [x] Native Windows PnP hot-plug detection (`WM_DEVICECHANGE`) with non-blocking recovery of disconnected magnetic modules.
- [x] Hardware sleep and unified standby power management on application shutdown and Windows suspend events.

### 2. Audio & Media Controls
- [x] Windows CoreAudio / WASAPI audio mixer subsystem (`WindowsAudioService`).
- [x] Hardware fader mapping to Master Volume, Default Microphone, or active application audio sessions.
- [x] Dynamic background discovery of running audio streams (Spotify, Discord, Chrome, games).
- [x] Windows Now Playing integration (`NowPlayingService`) with title/artist extraction and album art display.

### 3. Direct Hardware Peripherals
- [x] **Razer HID Driver**: Direct 90-byte Feature Report communication for DPI stages (400–30,000 DPI), polling rates (125–8000 Hz), onboard profiles, and battery status.
- [x] **Wooting Analog Keyboard Driver**: Direct ARM V3 HID control (`UsagePage 0x1337`) for instantaneous profile switching (Digital Mode & Analog Profiles 1–3, Rapid Trigger, Tachyon Mode).

### 4. Third-Party Integrations
- [x] **OBS Studio**: WebSocket v5 client with streaming/recording toggle, scene switching, audio source muting, and replay buffer.
- [x] **Voicemod**: Official Control API 1.0.0 (WebSocket + REST) with real-time voice changer toggle, soundboard playback, background voice FX toggle, "Hear Myself" monitor, and microphone mute.
- [x] **Discord**: RPC client for voice mute and deafen controls.
- [x] **Spotify**: Web API client with playback controls, shuffle, repeat, and volume adjustments.
- [x] **Twitch & Kick**: Streamer controls, chat messages, and live status badges.
- [x] **JetKVM**: Remote KVM input switching and video forwarding controls.
- [x] **VLC Media Player**: HTTP remote control.

### 5. Advanced Deck Capabilities
- [x] **Gestures**: Hardware key tap, double tap, long press, and hold detection (`KeyInteractionEngine`).
- [x] **Multi-Actions**: Chained macro action sequences with configurable step delays (`MultiActionStepModel`).
- [x] **Conditional Actions**: State-dependent execution logic (`ConditionalActionModel`).
- [x] **Animated Key Displays**: GIF and MP4 video loop decoding via `MediaFoundationVideoDecoder` and SkiaSharp frame streaming.
- [x] **Smart Profiles**: Automatic profile switching based on foreground active process (`SmartProfileService`).
- [x] **Dual-State Key Icons**: Independent visual state rendering for toggle actions (Default vs Active).

### 6. Security & Storage
- [x] **Windows DPAPI Encryption**: Hardware/user-backed encryption (`enc:v1:`) for all sensitive tokens and passwords (`SecretProtectionService`).
- [x] **Data Isolation**: All user settings, profiles, and crash logs stored strictly under `%APPDATA%\MasterHub\`.
- [x] **Profile Sanitization**: Automatic stripping of passwords and API keys on profile export.

### 7. Modern Presentation Layer (Avalonia 11)
- [x] Modern dark glassmorphism user interface adhering to clean MVVM architecture.
- [x] Modular decomposition ensuring all `.axaml` and `.axaml.cs` files stay strictly under 150 lines.
- [x] Dynamic multilingual localization engine supporting 6 languages (EN, PL, DE, ES, FR, JA).

---

## Future Considerations

- [ ] Linux (libusb) hardware driver support for cross-platform Linux workstations.
- [ ] Plugin SDK for third-party community extensions and custom action packs.
- [ ] Web-based cloud profile sharing repository (sanitized profiles only).

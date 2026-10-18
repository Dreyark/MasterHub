# MasterHub Hardware Protocol & Reverse Engineering Reference

## 1. Device Identification (USB PnP)

All Cooler Master MasterHub modular devices share the USB Vendor ID **`0x2516`** (`Cooler Master Technology Inc.`):

| Module Name | Model Code | Product ID (PID) | HID Report Handling | Description |
| :--- | :--- | :--- | :--- | :--- |
| **Base Station** | `MHBS01` | `0x01F6` | `MasterHubUsbTransport` | Base dock supplying power & magnetic slots |
| **Key Deck 15** | `MHKD15` / `M4315` | `0x01FE` | `MasterHubUsbTransport` | 15 LCD keys (3x5 grid), 72x72 px color screens |
| **Fader 5** | `MHFD05` / `M0_Slider` | `0x0200` | `MasterHubUsbTransport` | 5 motorized vertical sliders/faders (0..100) |
| **Roller 2** | `MHRR02` / `M0_Wheel` | `0x0204` | `MasterHubUsbTransport` | 2 horizontal rollers with dual-direction dials |
| **Display IPS** | `M131` / `M131E` | `0x131E` / `0x1312`| Windows Desktop Monitor | Ultra-wide IPS monitoring screen module |

---

## 2. 100% Pure Native C# USB HID Driver

MasterHub does **not** depend on any proprietary vendor DLLs, official companion apps, or Microsoft Visual C++ redistributables. All hardware communication is implemented natively in C# via standard Win32 USB HID APIs (`kernel32.dll`, `hid.dll`, `setupapi.dll`).

---

## 3. Reverse-Engineered USB HID Protocol

### 3.1. Key Deck 15 (`M4315`, PID `0x01FE`)

#### Software Mode Activation:
- **Report Type**: Feature Report
- **Report ID**: `0x03`
- **Command**: `0x3A`
- **Payload**: `0x01` (enable software mode / PC screen control), `0x00` (hardware mode)
- **Buffer Length**: 32 bytes (`0x20`)

```
[0x03, 0x3A, isSoftwareMode ? 0x01 : 0x00, 0x00, ...]
```

#### LCD Backlight Brightness:
- **Report Type**: Feature Report
- **Report ID**: `0x03`
- **Command**: `0x34`
- **Brightness**: `0..100` (`0` = Off, `100` = Maximum)
- **Buffer Length**: 32 bytes (`0x20`)

```
[0x03, 0x34, brightness (0..100), 0x00, ...]
```

#### Key LCD Image Transfer (`DisplayPictureOnButton`):
- **Report Type**: Output Report (`WriteFile` via interrupt/bulk OUT)
- **Report Size**: 320 bytes (`0x140`) per chunk
- **Header** (8 bytes):
  - `[0]`: Report ID = `0x02`
  - `[1]`: Opcode = `0x36`
  - `[2..3]`: Chunk index (16-bit uint, little-endian)
  - `[4..5]`: Chunk length (16-bit uint, little-endian, up to 312 bytes)
  - `[6]`: Button Index (`0..14`)
  - `[7]`: Final Chunk Flag (`1` if last chunk of the image, else `0`)
- **Payload**: Up to 312 bytes of raw JPEG image stream (`72x72` px)

#### Key Input Event (`ReadInputReport`):
- **Report Type**: Input Report
- **Report ID**: `0x02`
- **Report Length**: `0x0F` (15 bytes)
- **Data Layout**:
  - `buf[0]`: `0x02`
  - `buf[1]`: `0x0F`
  - `buf[2..16]`: Key press state for keys 0 through 14:
    - `0x01` = Key Pressed (Down)
    - `0x00` = Key Released (Up)

---

### 3.2. Fader 5 (`M005S`, PID `0x0200`)

#### Slider Movement Input Event (`ReadInputReport`):
- **Report Type**: Input Report
- **Report ID**: `0x02` (or `0x00`)
- **Report Length**: `0x05` (5 bytes)
- **Data Layout**:
  - `buf[2..6]`: Sliders 1 through 5 current values in range **`0` (bottom) to `100` (top)** directly.

#### Synchronous Slider Query (`GetSliderValues`):
- **Report Type**: Feature Report
- **Report ID**: `0x33`
- **Buffer Length**: 33 bytes (`0x21`)
- **Response**: `buf[2..6]` contains the 5 slider positions.

#### LED Color & Lighting Mode:
- **LED Mode**: Output Report `[0x02, 0x2C, Module=0x01, Index (0..4 or 0xFF), Mode (1=Static), SaveFw ? 1 : 0, ...]` (64 bytes)
- **LED Color**: Output Report `[0x02, 0x2D, Module=0x01, Index (0..4 or 0xFF), R, G, B, SaveFw ? 1 : 0, ...]` (64 bytes)
- **LED Brightness**: Output Report `[0x02, 0x2E, Module=0x01, Index (0..4 or 0xFF), Brightness (0..100), ...]` (64 bytes)

---

### 3.3. Roller 2 (`M032LE`, PID `0x0204`)

#### Roller Rotation Input Event (`ReadInputReport`):
- **Report Type**: Input Report
- **Report ID**: `0x01`
- **Report Length**: `0x02` (2 bytes)
- **Data Layout**:
  - `buf[2]`: Right Roller state:
    - `0` = Rotate Up (▲)
    - `1` = Rotate Down (▼)
    - `255` = Idle / No rotation
  - `buf[3]`: Left Roller state:
    - `0` = Rotate Up (▲)
    - `1` = Rotate Down (▼)
    - `255` = Idle / No rotation

---

### 3.4. Base Station (`M0064`, PID `0x01F6`)

- LED controls use the exact same output reports as the M0 Fader, targeting **`Module = 0x00`** and **`Index = 0x00`**.

---

## 4. Status & Return Codes

| Code | Meaning | Transport Action |
| :---: | :--- | :--- |
| `0` | **Success** | Data transferred or buffer received. |
| `2` | **Timeout / Idle** | Normal state when no physical interaction is occurring. |
| `7` | **Device Disconnected** | Physical detachment from magnetic dock or USB port. Triggers automatic async rescan. |

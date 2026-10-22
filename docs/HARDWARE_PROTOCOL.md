# MasterHub Hardware Protocol & Reverse Engineering Reference

> **Device Vendor ID**: `0x2516` (`Cooler Master Technology Inc.`)  
> **Driver Implementation**: 100% Pure Native C# Win32 USB HID (`kernel32.dll`, `hid.dll`, `setupapi.dll`).  
> **Zero Proprietary Dependencies**: No official companion apps, no vendor DLLs, no background drivers required.

---

## 1. Device Matrix & Hardware Identifiers

All modular hardware units connect to the magnetic bus of the Base Station or via dedicated USB-C ports:

| Module Name | Model Code | Product ID (PID) | HID Transport Interface | Inputs | Outputs / Displays |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Base Station** | `MHBS01` | `0x01F6` | `MasterHubUsbTransport` | Tilt orientation sensor (Feature `0x35`) | RGB LED Lightbar strip (`0x2C..0x2E`) |
| **Key Deck 15** | `MHKD15` | `0x01FE` | `MasterHubUsbTransport` | 15 physical keys (3×5 grid) | 15 LCDs 72×72 px + Backlight (min 1) |
| **Fader 5** | `MHFD05` | `0x0200` | `MasterHubUsbTransport` | 5 linear sliders (0..100) | 5 per-channel RGB LEDs |
| **Roller 2** | `MHRR02` | `0x0204` | `MasterHubUsbTransport` | 2 dual-direction rollers | **No hardware LEDs** |
| **3 Knobs** | `MHKB03` | `0x0202` | `MasterHubUsbTransport` | 3 endless encoders + clicks | 3 RGB LED rings (10 modes) |
| **Dial Display** | `MHDD01` | `0x0206` | `MasterHubUsbTransport` | 1 outer dial ring + click | 240×240 px circular IPS screen |
| **Ultra-Wide IPS**| `M131E` | `0x131E` / `0x1312` | Windows Display Driver | N/A | Windows secondary desktop |

---

## 2. Hardware Safety Guard (`IsForbiddenReport`)

To prevent permanent device bricking, loss of factory calibration, or premature flash memory degradation, `MasterHubDeviceHandle` enforces a strict hardware safety filter:

| Report ID | Opcode | Description | Block Reason / Consequence |
| :---: | :---: | :--- | :--- |
| `0x02` | `0x2A` | M0 Update Firmware | Bricking risk from corrupt firmware payload |
| `0x02` | `0x2B` | M0 Set Serial Number | Overwriting factory module serial numbers |
| `0x02` | `0x2F` | Calibrate_Direction | **Permanent destruction of factory tilt sensor calibration** |
| `0x02` | `0x31` | M0 Save Factory Data | Corrupts factory calibration storage |
| `0x02` | `0x34` | Keys Update Firmware | Bricking risk for Key Deck 15 MCU |
| `0x03` | `0x35` | Keys Set Serial Number | Overwriting factory Key Deck serial |
| `0x02` | `0x38` | Keys Save Screen to Flash | **Premature wear-out of SPI flash memory cells** |
| `0x02` | `0x44` | Dial Update Firmware | Bricking risk for Dial MCU |
| `0x03` | `0x45` | Dial Set Serial Number | Overwriting factory Dial serial |
| `0x02` | `0x47` | Dial Save Screen to Flash | **Premature wear-out of Dial SPI flash memory cells** |
| `0x02/0x03` | `0x48..0x4D` | Test Recovery / Factory Reset | Wipes factory parameters |

---

## 3. Key Deck 15 (`0x01FE`) Protocol

### 3.1. Software Mode Activation
Enables PC software to stream custom frames to the LCD displays:
- **Report Type**: Feature Report
- **Report ID**: `0x03`
- **Command**: `0x3A`
- **Buffer Layout (32 bytes)**: `[0x03, 0x3A, 0x01 (1=Software, 0=Hardware), 0x00, ...]`

### 3.2. Backlight Brightness
- **Report Type**: Feature Report
- **Report ID**: `0x03`, Command `0x34`
- **Brightness**: Clamped to `1..100` (`Math.Clamp(brightness, 1, 100)`).
- **CRITICAL NOTE**: Sending `0` causes Key Deck MCU lockup (`I/O Timeout`) requiring a physical USB reconnect. Blank screens are achieved via `FillScreen(0, 0, 0)` or black JPEG.

### 3.3. Key LCD Image Transfer (`0x02 0x36` — Individual 72×72 px)
Transfers JPEG images to individual LCD buttons in 320-byte chunks via interrupt/bulk OUT:
- **Header (8 bytes)**:
  - `[0]`: Report ID = `0x02`
  - `[1]`: Opcode = `0x36`
  - `[2..3]`: Chunk Index (16-bit uint, little-endian)
  - `[4..5]`: Chunk Payload Length (16-bit uint, little-endian, up to 312 bytes)
  - `[6]`: Button Index (`0..14`)
  - `[7]`: End-of-Image Flag (`1` if final chunk, else `0`)
- **Payload**: Up to 312 bytes of raw JPEG image stream.

### 3.4. Panoramic Panel Image Transfer (`0x02 0x35` — Full 480×272 px)
Transfers a single 480×272 continuous panoramic image across the entire 15-key grid in ~11 ms:
- **Header (8 bytes)**:
  - `[0]`: Report ID = `0x02`
  - `[1]`: Opcode = `0x35` (Panoramic full-panel command)
  - `[2..3]`: Chunk Index (16-bit uint)
  - `[4..5]`: Chunk Length (16-bit uint)
  - `[6]`: Reserved (`0x00`)
  - `[7]`: End-of-Image Flag (`1` if final chunk, else `0`)
- **Hardware Coordinate Grid**:
  - Each key button window is 79×75 px.
  - Slicing pitch: `x = (index % 5) * 102`, `y = (index / 5) * 100`.

### 3.5. Solid Color Commands
- **Fill Entire Deck (all 15 keys)**: Feature Report `[0x03, 0x36, r, g, b, 0x00, ...]`.
- **Fill Single Button**: Feature Report `[0x03, 0x37, buttonId (0..14), r, g, b, 0x00, ...]`.

### 3.6. Key Press Input Report (`ReadInputReport`)
- **Report Type**: Input Report (`0x02`, length 15 bytes)
- **Data Layout**:
  - `buf[0]`: `0x02`
  - `buf[1]`: `0x0F`
  - `buf[2..16]`: Key state for keys 0..14 (`0x01` = Pressed, `0x00` = Released).

---

## 4. Fader 5 (`0x0200`) Protocol

### 4.1. Slider Input Event (`ReadInputReport`)
- **Report Type**: Input Report (`0x02`, length 5 bytes)
- **Data Layout**: `buf[2..6]` contain current positions for Sliders 1 through 5 (`0` = bottom, `100` = top).

### 4.2. Synchronous Query (`GetSliderValues`)
- **Report Type**: Feature Report `0x33` (buffer length 32 bytes).
- **Response**: `buf[2..6]` contains the 5 slider positions.

### 4.3. Analog Deadband & Jitter Filter
```csharp
byte v = raw switch {
    <= 2 => 0,
    >= 98 => 100,
    _ => raw
};
if (last == v) return last;
if (Math.Abs((int)last - (int)v) < 2 && v != 0 && v != 100) return last;
return v;
```

### 4.4. LED Lighting Controls (64-byte Output Reports)
- **Lighting Mode (`0x2C`)**: `[0x02, 0x2C, Module=0x01, Channel (0..4 or 0xFF), Mode (1=Static), SaveFw ? 1 : 0, ...]`
- **RGB Color (`0x2D`)**: `[0x02, 0x2D, Module=0x01, Channel, R, G, B, SaveFw ? 1 : 0, ...]`
- **Brightness (`0x2E`)**: `[0x02, 0x2E, Module=0x01, Channel, Brightness (0..100), ...]`

---

## 5. Roller 2 (`0x0204`) Protocol

### 5.1. Roller Rotation Input Event
- **Report Type**: Input Report (`0x01`, length 2 bytes)
- **Hardware Mapping**:
  - `buf[2]`: **Right Roller** (`0` = Up ▲, `1` = Down ▼, `255` = Idle).
  - `buf[3]`: **Left Roller** (`0` = Up ▲, `1` = Down ▼, `255` = Idle).

### 5.2. Dynamic Velocity Acceleration
Rotary step multiplier based on tick delta:
- `< 50 ms`: Multiplier = **3×** (rapid flick)
- `< 110 ms`: Multiplier = **2×** (moderate speed)
- Default: Multiplier = **1×** (precise single-step click)

> [!NOTE]  
> The dual roller module contains **no physical LEDs**. Never send LED control packets to PID `0x0204`.

---

## 6. 3 Knobs Module (`0x0202`) Protocol

### 6.1. Encoder & Click Input Event
- **Report Type**: Input Report
- Returns delta rotation steps (CW / CCW) and click switch state for Knobs 0, 1, and 2.
- Evaluated with velocity acceleration (1×/2×/3×).

### 6.2. Addressable RGB LED Rings (64-byte Output Reports)
- **DeviceType**: `0x02` (3 Knobs)
- **Knob Indices**: `0..2`
- **LED Modes (`0x02 0x2C`)**:
  - `0`: Disable / Off
  - `1`: Static Color
  - `2`: Breathing
  - `3`: Strobe
  - `4`: Spring
  - `5`: Cycling
  - `6`: Random
  - `7`: Wave
  - `8`: Water Shimmer
  - `9`: Rainbow
- **RGB Color (`0x02 0x2D`)**: `[0x02, 0x2D, 0x02, KnobIndex, R, G, B, SaveFw, ...]`
- **Brightness (`0x02 0x2E`)**: `[0x02, 0x2E, 0x02, KnobIndex, Brightness (0..100), ...]`
- **Speed (`0x02 0x30`)**: `[0x02, 0x30, Speed (0..100), ...]`

---

## 7. Dial Display Module (`0x0206`) Protocol

### 7.1. Software Mode Activation
- **Feature Report**: `[0x03, 0x49, 0x01, 0x00, ...]`  
- **MANDATORY**: Must be dispatched immediately upon device connection to avoid controller lockup.

### 7.2. Dial Backlight
- **Feature Report**: `[0x03, 0x44, brightness (1..100), 0x00, ...]`
- Minimum safe brightness is `1`.

### 7.3. Solid Color Fill
- **Feature Report**: `[0x03, 0x46, r, g, b, 0x00, ...]`

### 7.4. Dial 240×240 IPS Image Transfer (`0x02 0x45`)
- Streams 240×240 pixel circular JPEG images in 320-byte Output Reports (312-byte payload chunks):
  - `report[0]`: `0x02` (Report ID)
  - `report[1]`: `0x45` (Opcode: Dial JPEG stream)
  - `report[2..3]`: Chunk Index (16-bit uint, little-endian)
  - `report[4..5]`: Chunk Length (16-bit uint, little-endian, up to 312 bytes)
  - `report[6]`: Reserved (`0x00`)
  - `report[7]`: `isLast` (`1` if final chunk, else `0`)
  - `report[8..319]`: Raw JPEG payload bytes

---

## 8. Base Station (`0x01F6`) & Tilt Orientation Sensor

### 8.1. Lightbar Strip
- Uses 64-byte Output Reports (`0x02 0x2C..0x2E`) with `DeviceType = 0x00` and `Index = 0x00`.

### 8.2. Tilt Orientation Sensor (Feature Report `0x35`)
- **Query**: Feature Report `0x35` (32 bytes).
- **Response Layout**: `[0x35, 0x00, value, ...]`:
  - `value == 1`: **Horizontal** (flat on desk, rotation angle 0°).
  - `value == 0`: **Vertical** (mounted upright on stand, rotation angle 90° / 180°).
- Automatically triggers UI canvas rotation and key layout remapping.

---

## 9. Native Third-Party Peripheral Drivers

### 9.1. Universal Razer Mouse HID
- **Vendor ID**: `0x1532`
- **Command Architecture**: 90-byte Feature Reports.
- **Capabilities**:
  - DPI stages: 400, 800, 1600, 3200, 6400, up to 30,000 DPI.
  - Polling rate: 125 Hz, 500 Hz, 1000 Hz, 2000 Hz, 4000 Hz, 8000 Hz.
  - Onboard memory profiles: 1..5.
  - Battery percentage and charging status telemetry.

### 9.2. Wooting Analog Keyboard Driver
- **Vendor ID**: `0x31E3`
- **HID Usage Pages**: `0x1337` (V2) / `0xFF55` (V3), Usage `0x0001`.
- **Capabilities**:
  - Digital Mode (Profile 0) & Analog Profiles 1..3 switching.
  - Rapid trigger, Tachyon mode, and dynamic actuation.
  - RGB memory buffer sync (`CmdRefreshRgbColors`).

---

## 10. Status & Return Codes

| Code | Status | Meaning & Transport Action |
| :---: | :--- | :--- |
| `0` | **Success** | Packet sent or input report received successfully. |
| `2` | **Timeout / Idle** | Normal non-blocking state when no physical interaction is occurring. |
| `7` | **Module Disconnected** | Physical detachment from magnetic dock. Triggers non-blocking async PnP rescan. |

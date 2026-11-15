using System;

namespace MasterHub.Core.Native;

/// <summary>
/// Pure builders of every HID report MasterHub sends to a module (no I/O, no state).
/// Keeping them in one static class makes them testable without hardware: a test feeds sample
/// arguments of every builder through <see cref="MasterHubDeviceHandle.IsForbiddenReport"/>
/// and proves that no code path can produce a command that is on the Safety-Guard blacklist.
/// </summary>
public static class ReportBuilders
{
    /// <summary>Total length of a picture output report.</summary>
    public const int PictureReportLength = 320;

    /// <summary>Payload bytes per picture chunk (320 - 8 header bytes).</summary>
    public const int PictureChunkMax = 312;

    public const byte OpcodeKeyPicture = 0x36;
    public const byte OpcodePanoramaPicture = 0x35;
    public const byte OpcodeDialPicture = 0x45;

    // ── Key Deck 15 (feature reports, id 0x03) ──────────────────────────────────────────────

    public static byte[] SetSoftwareMode(bool isSoftwareMode) =>
        Feature32(0x03, 0x3A, (byte)(isSoftwareMode ? 0x01 : 0x00));

    /// <summary>0 locks the key MCU (I/O timeout) – the minimum safe value is 1.</summary>
    public static byte[] KeyBacklight(byte brightness) =>
        Feature32(0x03, 0x34, Math.Clamp(brightness, (byte)1, (byte)100));

    public static byte[] FillKeyScreen(byte r, byte g, byte b) => Feature32(0x03, 0x36, r, g, b);

    public static byte[] FillSingleKey(byte buttonId, byte r, byte g, byte b) =>
        Feature32(0x03, 0x37, buttonId, r, g, b);

    // ── Dial (feature reports, id 0x03) ─────────────────────────────────────────────────────

    public static byte[] SetDialSoftwareMode(bool isSoftwareMode) =>
        Feature32(0x03, 0x49, (byte)(isSoftwareMode ? 0x01 : 0x00));

    /// <summary>0 locks the dial MCU (I/O timeout) – the minimum safe value is 1.</summary>
    public static byte[] DialBacklight(byte brightness) =>
        Feature32(0x03, 0x44, Math.Clamp(brightness, (byte)1, (byte)100));

    public static byte[] FillDialScreen(byte r, byte g, byte b) => Feature32(0x03, 0x46, r, g, b);

    // ── M0 family: base, faders, knobs, rollers (output reports, id 0x02, 64 bytes) ─────────

    public static byte[] LedMode(byte deviceType, byte ledIndex, byte mode, bool persist) =>
        Output64(0x2C, deviceType, ledIndex, mode, (byte)(persist ? 1 : 0));

    public static byte[] LedColor(byte deviceType, byte ledIndex, byte r, byte g, byte b, bool persist) =>
        Output64(0x2D, deviceType, ledIndex, r, g, b, (byte)(persist ? 1 : 0));

    public static byte[] LedBrightness(byte deviceType, byte ledIndex, byte brightness) =>
        Output64(0x2E, deviceType, ledIndex, Math.Clamp(brightness, (byte)0, (byte)100));

    public static byte[] LedSpeed(byte speed) =>
        Output64(0x30, Math.Clamp(speed, (byte)0, (byte)100));

    // ── Pictures (chunked output reports, 320 bytes) ────────────────────────────────────────

    /// <summary>Creates the reusable 320-byte buffer of a picture transfer (opcode and tag are fixed per picture).</summary>
    public static byte[] NewPictureReport(byte opcode, byte tag)
    {
        var report = new byte[PictureReportLength];
        report[0] = 0x02;
        report[1] = opcode;
        report[6] = tag; // key index for key pictures, 0 for panorama / dial
        return report;
    }

    /// <summary>Fills one chunk of a picture into <paramref name="report"/> (header + payload, rest zeroed).</summary>
    public static void FillPictureChunk(byte[] report, ushort chunkIndex, byte[] source, int sourceOffset, int chunkLen, bool isLast)
    {
        report[2] = (byte)(chunkIndex & 0xFF);
        report[3] = (byte)((chunkIndex >> 8) & 0xFF);
        report[4] = (byte)(chunkLen & 0xFF);
        report[5] = (byte)((chunkLen >> 8) & 0xFF);
        report[7] = (byte)(isLast ? 1 : 0);

        Buffer.BlockCopy(source, sourceOffset, report, 8, chunkLen);
        if (chunkLen < PictureChunkMax)
        {
            Array.Clear(report, 8 + chunkLen, PictureReportLength - (8 + chunkLen));
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private static byte[] Feature32(byte id, byte cmd, params byte[] args)
    {
        var rep = new byte[32];
        rep[0] = id;
        rep[1] = cmd;
        Array.Copy(args, 0, rep, 2, args.Length);
        return rep;
    }

    private static byte[] Output64(byte cmd, params byte[] args)
    {
        var rep = new byte[64];
        rep[0] = 0x02;
        rep[1] = cmd;
        Array.Copy(args, 0, rep, 2, args.Length);
        return rep;
    }
}

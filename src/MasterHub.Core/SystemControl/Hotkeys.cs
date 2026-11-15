using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MasterHub.Core.SystemControl;

/// <summary>How a hotkey is sent (T-10).</summary>
public enum HotkeySendMode
{
    /// <summary>As before: virtual-key events (what normal applications read).</summary>
    Normal = 0,

    /// <summary>
    /// Hardware <b>scan codes</b>, each key held for a short time: games that read the keyboard through DirectInput / raw input
    /// ignore plain virtual-key events and short taps. A game that runs elevated needs an elevated MasterHub too.
    /// </summary>
    ForGames = 1
}

/// <summary>A parsed hotkey: modifier virtual keys and one main key.</summary>
public sealed record HotkeyCombo(IReadOnlyList<byte> Modifiers, byte MainKey);

/// <summary>Reads hotkey text such as <c>Ctrl+Shift+S</c>, <c>Alt F4</c>, <c>F13</c>, <c>Win+D</c>.</summary>
public static class HotkeyParser
{
    public const byte VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B;

    public static HotkeyCombo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var modifiers = new List<byte>();
        byte main = 0;

        foreach (string raw in text.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string p = raw.Trim().ToLowerInvariant();
            switch (p)
            {
                case "ctrl" or "control": Add(modifiers, VkControl); break;
                case "shift": Add(modifiers, VkShift); break;
                case "alt": Add(modifiers, VkMenu); break;
                case "win" or "windows" or "meta": Add(modifiers, VkLWin); break;
                case "space": main = 0x20; break;
                case "enter" or "return": main = 0x0D; break;
                case "tab": main = 0x09; break;
                case "esc" or "escape": main = 0x1B; break;
                case "del" or "delete": main = 0x2E; break;
                case "backspace": main = 0x08; break;
                case "home": main = 0x24; break;
                case "end": main = 0x23; break;
                case "pageup": main = 0x21; break;
                case "pagedown": main = 0x22; break;
                case "up": main = 0x26; break;
                case "down": main = 0x28; break;
                case "left": main = 0x25; break;
                case "right": main = 0x27; break;
                case "insert": main = 0x2D; break;
                default:
                    if (p.Length >= 2 && p[0] == 'f' && int.TryParse(p[1..], out int f) && f is >= 1 and <= 24) main = (byte)(0x6F + f);
                    else if (p.Length == 1 && char.IsAsciiLetterOrDigit(p[0])) main = (byte)char.ToUpperInvariant(p[0]);
                    break;
            }
        }

        return main == 0 && modifiers.Count == 0 ? null : new HotkeyCombo(modifiers, main);
    }

    private static void Add(List<byte> list, byte vk)
    {
        if (!list.Contains(vk)) list.Add(vk);
    }

    /// <summary>Keys that need the "extended" flag (arrows, navigation block, Insert/Delete, Windows keys).</summary>
    public static bool IsExtended(byte vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E => true,
        0x5B or 0x5C or 0xA3 or 0xA5 => true,
        _ => false
    };
}

/// <summary>Sends single keys; the scan-code variant fills the hardware scan code and sets <c>KEYEVENTF_SCANCODE</c>.</summary>
public interface IKeyboardSender
{
    void Down(byte vk, bool extended, bool scanCode);
    void Up(byte vk, bool extended, bool scanCode);
}

/// <summary>The "For games" sequence: modifiers down, main key down, held, main key up, modifiers up (always, even after an error).</summary>
public sealed class HotkeySender
{
    /// <summary>Default hold of the main key (~30 ms; raise to 50–80 ms if a game misses presses).</summary>
    public const int DefaultHoldMs = 30;
    public const int MinHoldMs = 10;
    public const int MaxHoldMs = 500;

    private readonly IKeyboardSender _keyboard;
    private readonly Action<int> _sleep;

    public HotkeySender(IKeyboardSender keyboard, Action<int>? sleep = null)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _sleep = sleep ?? Thread.Sleep;
    }

    /// <summary>Returns <c>false</c> when the text is not a hotkey.</summary>
    public bool Send(string hotkey, HotkeySendMode mode, int holdMs = 0)
    {
        var combo = HotkeyParser.Parse(hotkey);
        if (combo == null) return false;

        bool scan = mode == HotkeySendMode.ForGames;
        int hold = Math.Clamp(holdMs <= 0 ? DefaultHoldMs : holdMs, MinHoldMs, MaxHoldMs);

        var pressed = new List<byte>();
        try
        {
            foreach (byte mod in combo.Modifiers)
            {
                _keyboard.Down(mod, HotkeyParser.IsExtended(mod), scan);
                pressed.Add(mod);
            }
            _sleep(15);

            if (combo.MainKey != 0)
            {
                _keyboard.Down(combo.MainKey, HotkeyParser.IsExtended(combo.MainKey), scan);
                _sleep(hold);
                _keyboard.Up(combo.MainKey, HotkeyParser.IsExtended(combo.MainKey), scan);
            }
            _sleep(15);
        }
        finally
        {
            // never leave a modifier "stuck" after an exception
            for (int i = pressed.Count - 1; i >= 0; i--) _keyboard.Up(pressed[i], HotkeyParser.IsExtended(pressed[i]), scan);
        }
        return true;
    }
}

/// <summary><c>SendInput</c> keyboard: with <c>scanCode</c> the virtual key is replaced by the hardware scan code (<c>KEYEVENTF_SCANCODE</c>).</summary>
public sealed class WindowsKeyboardSender : IKeyboardSender
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfExtendedKey = 0x1, KeyeventfKeyUp = 0x2, KeyeventfScanCode = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;   // keeps the union at its real size (40-byte INPUT on x64)
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public void Down(byte vk, bool extended, bool scanCode) => Send(vk, extended, scanCode, up: false);

    public void Up(byte vk, bool extended, bool scanCode) => Send(vk, extended, scanCode, up: true);

    private static void Send(byte vk, bool extended, bool scanCode, bool up)
    {
        uint scan = MapVirtualKeyW(vk, 0 /* MAPVK_VK_TO_VSC */);
        uint flags = (extended ? KeyeventfExtendedKey : 0) | (up ? KeyeventfKeyUp : 0) | (scanCode ? KeyeventfScanCode : 0);

        var input = new INPUT
        {
            type = InputKeyboard,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = scanCode ? (ushort)0 : vk, wScan = (ushort)scan, dwFlags = flags } }
        };
        SendInput(1, new[] { input }, InputSize);
    }
}

using System;
using System.Runtime.InteropServices;

namespace MasterHub.Core.Macros;

/// <summary>
/// Wstrzykiwanie wejścia przez SendInput. Każde zdarzenie niesie znacznik <see cref="Marker"/> w dwExtraInfo,
/// dzięki czemu nasze własne haki (wyzwalacze, nagrywarka) mogą je odróżnić i nie wpadają w pętlę sprzężenia zwrotnego.
/// </summary>
public sealed class WindowsInputSimulator : IInputSimulator
{
    /// <summary>"MHUM" - znacznik zdarzeń wygenerowanych przez MasterHub.</summary>
    public static readonly UIntPtr Marker = new(0x4D48554D);

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10,
        MOUSEEVENTF_MIDDLEDOWN = 0x20, MOUSEEVENTF_MIDDLEUP = 0x40, MOUSEEVENTF_XDOWN = 0x80, MOUSEEVENTF_XUP = 0x100,
        MOUSEEVENTF_WHEEL = 0x800, MOUSEEVENTF_HWHEEL = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    private static void Send(INPUT input) => SendInput(1, new[] { input }, InputSize);

    public void KeyDown(int vk, bool extended) => SendKey(vk, extended, up: false);
    public void KeyUp(int vk, bool extended) => SendKey(vk, extended, up: true);

    private static void SendKey(int vk, bool extended, bool up)
    {
        if (vk <= 0 || vk > 0xFF) return;
        uint scan = MapVirtualKeyW((uint)vk, 0 /* MAPVK_VK_TO_VSC */);
        uint flags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0);
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)scan, dwFlags = flags, dwExtraInfo = Marker } }
        };
        Send(input);
    }

    public void MouseDown(MacroMouseButton button) => SendMouse(button, up: false);
    public void MouseUp(MacroMouseButton button) => SendMouse(button, up: true);

    private static void SendMouse(MacroMouseButton button, bool up)
    {
        uint flags;
        uint data = 0;
        switch (button)
        {
            case MacroMouseButton.Left: flags = up ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_LEFTDOWN; break;
            case MacroMouseButton.Right: flags = up ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_RIGHTDOWN; break;
            case MacroMouseButton.Middle: flags = up ? MOUSEEVENTF_MIDDLEUP : MOUSEEVENTF_MIDDLEDOWN; break;
            case MacroMouseButton.X1: flags = up ? MOUSEEVENTF_XUP : MOUSEEVENTF_XDOWN; data = 1; break;
            case MacroMouseButton.X2: flags = up ? MOUSEEVENTF_XUP : MOUSEEVENTF_XDOWN; data = 2; break;
            default: return;
        }
        Send(new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = Marker } } });
    }

    public void Wheel(int delta, bool horizontal)
    {
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT { dwFlags = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, mouseData = unchecked((uint)delta), dwExtraInfo = Marker }
            }
        });
    }

    public void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (char ch in text)
        {
            var down = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE, dwExtraInfo = Marker } } };
            var up = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, dwExtraInfo = Marker } } };
            SendInput(2, new[] { down, up }, InputSize);
        }
    }
}

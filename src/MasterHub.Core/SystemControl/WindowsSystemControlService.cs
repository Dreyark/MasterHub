using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MasterHub.Core.SystemControl;

public class WindowsSystemControlService : ISystemControlService
{
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_STOP = 0xB2;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;

    private const byte VK_LWIN = 0x5B;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12;   // Alt
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_LEFT = 0x25;
    private const byte VK_RIGHT = 0x27;
    private const byte VK_TAB = 0x09;
    private const byte VK_D = 0x44;
    private const byte VK_V = 0x56;
    private const byte VK_F4 = 0x73;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private static bool IsExtendedKey(byte vk)
    {
        return vk switch
        {
            0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E => true,
            0x5B or 0x5C => true,
            0xA3 or 0xA5 => true,
            _ => false
        };
    }

    private static void SendKeyEvent(byte vk, bool keyUp)
    {
        byte scan = (byte)MapVirtualKey(vk, 0);
        uint flags = 0;
        if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        if (keyUp) flags |= KEYEVENTF_KEYUP;
        keybd_event(vk, scan, flags, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private static void PressKey(byte vk)
    {
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // --- Multimedia ---
    public void PlayPause() => PressKey(VK_MEDIA_PLAY_PAUSE);
    public void NextTrack() => PressKey(VK_MEDIA_NEXT_TRACK);
    public void PreviousTrack() => PressKey(VK_MEDIA_PREV_TRACK);
    public void StopTrack() => PressKey(VK_MEDIA_STOP);
    public void VolumeUp() => PressKey(VK_VOLUME_UP);
    public void VolumeDown() => PressKey(VK_VOLUME_DOWN);
    public void MuteVolume() => PressKey(VK_VOLUME_MUTE);

    // --- Mysz (scroll) ---
    public void ScrollVertical(int clicks)
    {
        uint data = (uint)(clicks * 120);
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, data, UIntPtr.Zero);
    }

    public void ScrollHorizontal(int clicks)
    {
        uint data = (uint)(clicks * 120);
        mouse_event(MOUSEEVENTF_HWHEEL, 0, 0, data, UIntPtr.Zero);
    }

    // --- System ---
    public void TakeScreenshot()
    {
        // Win + Shift + S
        keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(0x53, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // 'S'

        keybd_event(0x53, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public void OpenTaskManager()
    {
        // Ctrl + Shift + Esc
        keybd_event(VK_CONTROL, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);

        keybd_event(VK_ESCAPE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public void OpenCalculator() => LaunchProcess("calc.exe");
    public void OpenDefaultBrowser() => LaunchProcess("https://www.google.com");

    public void LaunchProcess(string pathOrCommand)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = pathOrCommand,
                UseShellExecute = true
            });
        }
        catch { }
    }

    // --- ETAP 1: IMPLEMENTACJE ---

    public void OpenApplication(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void OpenWebsite(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void CloseApplication(string processNameOrTitle = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(processNameOrTitle))
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
            else
            {
                string procName = System.IO.Path.GetFileNameWithoutExtension(processNameOrTitle).Trim();
                var procs = Process.GetProcessesByName(procName);
                if (procs.Length == 0 && processNameOrTitle.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    procName = processNameOrTitle[..^4].Trim();
                    procs = Process.GetProcessesByName(procName);
                }

                foreach (var proc in procs)
                {
                    try
                    {
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            SendMessage(proc.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        }
                        else
                        {
                            proc.CloseMainWindow();
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            // Set text to Windows Clipboard via native Win32 API
            bool clipboardSet = false;
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    int byteLength = (text.Length + 1) * 2;
                    IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteLength);
                    if (hGlobal != IntPtr.Zero)
                    {
                        IntPtr target = GlobalLock(hGlobal);
                        if (target != IntPtr.Zero)
                        {
                            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                            Marshal.WriteInt16(target, text.Length * 2, 0); // null terminator
                            GlobalUnlock(hGlobal);
                            if (SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero)
                            {
                                clipboardSet = true;
                            }
                        }
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }

            if (clipboardSet)
            {
                Thread.Sleep(30);
                // Send Ctrl+V
                keybd_event(VK_CONTROL, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                byte vkV = 0x56;
                keybd_event(vkV, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(vkV, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            else
            {
                // Fallback: SendInput Unicode keystrokes directly
                foreach (char ch in text)
                {
                    keybd_event(0, (byte)ch, KEYEVENTF_UNICODE, UIntPtr.Zero);
                    keybd_event(0, (byte)ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }
        }
        catch { }
    }

    public void SendHotkey(string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString)) return;
        try
        {
            var parts = hotkeyString.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var modifiers = new System.Collections.Generic.List<byte>();
            byte mainKey = 0;

            foreach (var part in parts)
            {
                string p = part.Trim().ToLowerInvariant();
                switch (p)
                {
                    case "ctrl" or "control":
                        if (!modifiers.Contains(VK_CONTROL)) modifiers.Add(VK_CONTROL);
                        break;
                    case "shift":
                        if (!modifiers.Contains(VK_SHIFT)) modifiers.Add(VK_SHIFT);
                        break;
                    case "alt":
                        if (!modifiers.Contains(VK_MENU)) modifiers.Add(VK_MENU);
                        break;
                    case "win" or "windows" or "meta":
                        if (!modifiers.Contains(VK_LWIN)) modifiers.Add(VK_LWIN);
                        break;
                    case "space": mainKey = 0x20; break;
                    case "enter" or "return": mainKey = 0x0D; break;
                    case "tab": mainKey = 0x09; break;
                    case "esc" or "escape": mainKey = 0x1B; break;
                    case "del" or "delete": mainKey = 0x2E; break;
                    case "backspace": mainKey = 0x08; break;
                    case "home": mainKey = 0x24; break;
                    case "end": mainKey = 0x23; break;
                    case "pageup": mainKey = 0x21; break;
                    case "pagedown": mainKey = 0x22; break;
                    case "up": mainKey = 0x26; break;
                    case "down": mainKey = 0x28; break;
                    case "left": mainKey = 0x25; break;
                    case "right": mainKey = 0x27; break;
                    case "insert": mainKey = 0x2D; break;
                    case "f1": mainKey = 0x70; break;
                    case "f2": mainKey = 0x71; break;
                    case "f3": mainKey = 0x72; break;
                    case "f4": mainKey = 0x73; break;
                    case "f5": mainKey = 0x74; break;
                    case "f6": mainKey = 0x75; break;
                    case "f7": mainKey = 0x76; break;
                    case "f8": mainKey = 0x77; break;
                    case "f9": mainKey = 0x78; break;
                    case "f10": mainKey = 0x79; break;
                    case "f11": mainKey = 0x7A; break;
                    case "f12": mainKey = 0x7B; break;
                    case "f13": mainKey = 0x7C; break;
                    case "f14": mainKey = 0x7D; break;
                    case "f15": mainKey = 0x7E; break;
                    case "f16": mainKey = 0x7F; break;
                    case "f17": mainKey = 0x80; break;
                    case "f18": mainKey = 0x81; break;
                    case "f19": mainKey = 0x82; break;
                    case "f20": mainKey = 0x83; break;
                    case "f21": mainKey = 0x84; break;
                    case "f22": mainKey = 0x85; break;
                    case "f23": mainKey = 0x86; break;
                    case "f24": mainKey = 0x87; break;
                    default:
                        if (p.Length == 1 && char.IsLetterOrDigit(p[0]))
                        {
                            mainKey = (byte)char.ToUpperInvariant(p[0]);
                        }
                        break;
                }
            }

            if (mainKey == 0 && modifiers.Count == 0) return;

            // Press modifiers
            foreach (var mod in modifiers)
            {
                SendKeyEvent(mod, false);
            }
            Thread.Sleep(15);

            // Press main key
            if (mainKey != 0)
            {
                SendKeyEvent(mainKey, false);
                Thread.Sleep(30);
                SendKeyEvent(mainKey, true);
            }
            Thread.Sleep(15);

            // Release modifiers
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                SendKeyEvent(modifiers[i], true);
            }
        }
        catch { }
    }

    public void SystemSleep()
    {
        try
        {
            // Suspend (Standby/Sleep) via native API
            SetSuspendState(false, false, false);
        }
        catch
        {
            // Fallback via rundll32
            LaunchProcess("rundll32.exe powrprof.dll,SetSuspendState 0,1,0");
        }
    }

    public void SystemShutdown()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/s /t 0",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    public void SystemRestart()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 0",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    public void SystemLockScreen()
    {
        try
        {
            LockWorkStation();
        }
        catch { }
    }

    public void SystemScreensaver()
    {
        try
        {
            // WM_SYSCOMMAND (0x0112), SC_SCREENSAVE (0xF140) to HWND_BROADCAST (0xFFFF)
            PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF140), IntPtr.Zero);
        }
        catch { }
    }

    private void SendWinCtrlKey(byte vk)
    {
        try
        {
            SendKeyEvent(VK_LWIN, false);
            SendKeyEvent(VK_CONTROL, false);
            Thread.Sleep(20);

            SendKeyEvent(vk, false);
            Thread.Sleep(35);
            SendKeyEvent(vk, true);
            Thread.Sleep(20);
        }
        catch { }
        finally
        {
            SendKeyEvent(VK_CONTROL, true);
            SendKeyEvent(VK_LWIN, true);
        }
    }

    public void VirtualDesktopNext() => SendWinCtrlKey(VK_RIGHT);
    public void VirtualDesktopPrev() => SendWinCtrlKey(VK_LEFT);
    public void VirtualDesktopCreate() => SendWinCtrlKey(VK_D);
    public void VirtualDesktopClose() => SendWinCtrlKey(VK_F4);

    private void SendWinKey(byte vk)
    {
        try
        {
            SendKeyEvent(VK_LWIN, false);
            Thread.Sleep(20);

            SendKeyEvent(vk, false);
            Thread.Sleep(35);
            SendKeyEvent(vk, true);
            Thread.Sleep(20);
        }
        catch { }
        finally
        {
            SendKeyEvent(VK_LWIN, true);
        }
    }

    public void TaskView() => SendWinKey(VK_TAB);
    public void ShowDesktop() => SendWinKey(VK_D);
    public void OpenClipboardHistory() => SendWinKey(VK_V);
}

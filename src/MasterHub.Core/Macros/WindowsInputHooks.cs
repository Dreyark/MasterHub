using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MasterHub.Core.Macros;

/// <summary>
/// Globalne haki WH_KEYBOARD_LL / WH_MOUSE_LL na własnym wątku z pętlą komunikatów + okno Raw Input, które mówi,
/// czy ostatnio aktywną myszą było urządzenie Razer (VID_1532). Haki nie podają źródła zdarzenia, a Raw Input nie
/// pozwala niczego blokować - łączymy więc oba: Raw Input ustawia flagę "aktywna mysz", a hak ją odczytuje.
/// Wątek i haki istnieją tylko wtedy, gdy są aktywne przypisania makr lub trwa nagrywanie (zero kosztu na co dzień).
/// </summary>
public sealed class WindowsInputHooks : IInputEventSource
{
    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    private const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205,
        WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_MOUSEWHEEL = 0x20A, WM_XBUTTONDOWN = 0x20B, WM_XBUTTONUP = 0x20C, WM_MOUSEHWHEEL = 0x20E;
    private const uint LLKHF_EXTENDED = 0x1, LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x1;
    private const int WM_INPUT = 0x00FF, WM_QUIT = 0x0012;
    private const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIM_TYPEMOUSE = 0, RIDEV_INPUTSINK = 0x100;
    private const string RazerVid = "VID_1532";

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? lpModuleName);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string? name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string cls, IntPtr inst);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint command, StringBuilder? data, ref uint size);

    private Func<InputEvent, bool>? _intercept;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _kbHook, _mouseHook, _hwnd;
    private HookProc? _kbProc, _mouseProc;   // trzymane w polach - GC nie może zebrać delegatów haków
    private WndProc? _wndProc;
    private volatile bool _lastMouseIsRazer;
    private readonly Dictionary<IntPtr, bool> _deviceIsRazer = new();
    private readonly object _lifecycle = new();
    private ManualResetEventSlim? _ready;

    public bool IsRunning => _thread is { IsAlive: true };

    public void Start(Func<InputEvent, bool> intercept)
    {
        lock (_lifecycle)
        {
            _intercept = intercept ?? throw new ArgumentNullException(nameof(intercept));
            if (IsRunning) return;

            _ready = new ManualResetEventSlim(false);
            _thread = new Thread(HookThreadMain) { IsBackground = true, Name = "MasterHub-InputHooks" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait(TimeSpan.FromSeconds(3));
        }
    }

    public void Stop()
    {
        Thread? t;
        lock (_lifecycle)
        {
            t = _thread;
            if (t == null) return;
            if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        t.Join(TimeSpan.FromSeconds(2));
        lock (_lifecycle)
        {
            _thread = null;
            _threadId = 0;
        }
    }

    private void HookThreadMain()
    {
        _threadId = GetCurrentThreadId();
        string className = "MasterHubRawInput_" + Environment.ProcessId;
        IntPtr hInst = GetModuleHandleW(null);
        try
        {
            _kbProc = KeyboardHookProc;
            _mouseProc = MouseHookProc;
            string? moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
            IntPtr hMod = GetModuleHandleW(moduleName);
            _kbHook = SetWindowsHookExW(WH_KEYBOARD_LL, _kbProc, hMod, 0);
            _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, hMod, 0);

            CreateRawInputWindow(className, hInst);
        }
        catch
        {
            // brak haków = makra po prostu nie reagują; nie wywracamy aplikacji
        }
        finally
        {
            _ready?.Set();
        }

        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _kbHook = _mouseHook = IntPtr.Zero;
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
        UnregisterClassW(className, hInst);
    }

    private void CreateRawInputWindow(string className, IntPtr hInst)
    {
        _wndProc = RawInputWndProc;
        var wc = new WNDCLASSW { lpfnWndProc = _wndProc, hInstance = hInst, lpszClassName = className };
        if (RegisterClassW(ref wc) == 0) return;

        _hwnd = CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, hInst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return;

        var rid = new[] { new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = _hwnd } };
        RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private IntPtr RawInputWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            try { OnRawInput(lParam); } catch { }
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void OnRawInput(IntPtr hRawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 1024) return;

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, buf, ref size, headerSize) != size) return;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
            if (header.dwType != RIM_TYPEMOUSE) return;
            _lastMouseIsRazer = IsRazerDevice(header.hDevice);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private bool IsRazerDevice(IntPtr hDevice)
    {
        if (_deviceIsRazer.TryGetValue(hDevice, out bool known)) return known;

        bool razer = false;
        uint len = 0;
        GetRawInputDeviceInfoW(hDevice, RIDI_DEVICENAME, null, ref len);
        if (len > 0 && len < 1024)
        {
            var sb = new StringBuilder((int)len);
            if (GetRawInputDeviceInfoW(hDevice, RIDI_DEVICENAME, sb, ref len) != unchecked((uint)-1))
            {
                razer = sb.ToString().Contains(RazerVid, StringComparison.OrdinalIgnoreCase);
            }
        }
        _deviceIsRazer[hDevice] = razer;
        return razer;
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var ev = new InputEvent(
                    InputEventKind.Key, (int)k.vkCode, msg is WM_KEYDOWN or WM_SYSKEYDOWN, 0,
                    (k.flags & LLKHF_EXTENDED) != 0, (k.flags & LLKHF_INJECTED) != 0,
                    k.dwExtraInfo == WindowsInputSimulator.Marker, false);
                if (Dispatch(ev)) return new IntPtr(1);
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            // WM_MOUSEMOVE (0x200) i inne zdarzenia pomijamy natychmiast - hak jest wywoływany setki razy na sekundę.
            if (msg is WM_LBUTTONDOWN or WM_LBUTTONUP or WM_RBUTTONDOWN or WM_RBUTTONUP or WM_MBUTTONDOWN or WM_MBUTTONUP
                or WM_XBUTTONDOWN or WM_XBUTTONUP or WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
            {
                var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                bool injected = (m.flags & LLMHF_INJECTED) != 0;
                bool own = m.dwExtraInfo == WindowsInputSimulator.Marker;
                bool razer = _lastMouseIsRazer;
                int hi = (short)((m.mouseData >> 16) & 0xFFFF);

                InputEvent? ev = msg switch
                {
                    WM_LBUTTONDOWN => new InputEvent(InputEventKind.MouseButton, (int)MacroMouseButton.Left, true, 0, false, injected, own, razer),
                    WM_LBUTTONUP => new InputEvent(InputEventKind.MouseButton, (int)MacroMouseButton.Left, false, 0, false, injected, own, razer),
                    WM_RBUTTONDOWN => new InputEvent(InputEventKind.MouseButton, (int)MacroMouseButton.Right, true, 0, false, injected, own, razer),
                    WM_RBUTTONUP => new InputEvent(InputEventKind.MouseButton, (int)MacroMouseButton.Right, false, 0, false, injected, own, razer),
                    WM_MBUTTONDOWN => new InputEvent(InputEventKind.MouseButton, (int)MacroMouseButton.Middle, true, 0, false, injected, own, razer),
                    WM_MBUTTONUP => new InputEvent(InputEventKind.MouseButton, (int)MacroMouseButton.Middle, false, 0, false, injected, own, razer),
                    WM_XBUTTONDOWN => new InputEvent(InputEventKind.MouseButton, hi == 1 ? (int)MacroMouseButton.X1 : (int)MacroMouseButton.X2, true, 0, false, injected, own, razer),
                    WM_XBUTTONUP => new InputEvent(InputEventKind.MouseButton, hi == 1 ? (int)MacroMouseButton.X1 : (int)MacroMouseButton.X2, false, 0, false, injected, own, razer),
                    WM_MOUSEWHEEL => new InputEvent(InputEventKind.Wheel, 0, true, hi, false, injected, own, razer),
                    WM_MOUSEHWHEEL => new InputEvent(InputEventKind.HorizontalWheel, 0, true, hi, false, injected, own, razer),
                    _ => null
                };

                if (ev.HasValue && Dispatch(ev.Value)) return new IntPtr(1);
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private bool Dispatch(InputEvent ev)
    {
        try { return _intercept?.Invoke(ev) ?? false; }
        catch { return false; }   // wyjątek w odbiorcy nigdy nie może zablokować wejścia użytkownika
    }

    public void Dispose() => Stop();
}

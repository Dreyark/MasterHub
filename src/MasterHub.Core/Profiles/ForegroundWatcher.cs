using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MasterHub.Core.Profiles;

/// <summary>
/// Event-driven "the foreground window changed" signal (zero idle cost): <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>
/// on a thread of its own with a message loop, plus – only when a window-title rule exists –
/// <c>EVENT_OBJECT_NAMECHANGE</c> of the foreground window (browser tabs). The hook alone misses some
/// activations (Chrome sometimes activates without the event), so a cheap safety net compares
/// <c>GetForegroundWindow()</c> every 250 ms. Nothing runs while the watcher is stopped.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const uint WM_QUIT = 0x0012;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint threadId, uint timeMs);

    private readonly object _lock = new();
    private readonly WinEventProc _callback; // kept in a field: the native hook must never outlive the delegate
    private Thread? _thread;
    private uint _threadId;
    private Timer? _safetyNet;
    private IntPtr _lastForeground;
    private bool _watchTitles;
    private bool _disposed;

    public ForegroundWatcher() => _callback = OnWinEvent;

    /// <summary>Raised on a background thread whenever the foreground window (or, with title watching, its title) changed.</summary>
    public event Action? Changed;

    public bool IsRunning
    {
        get { lock (_lock) { return _thread != null; } }
    }

    /// <summary>Starts (or reconfigures) watching. Cheap to call repeatedly.</summary>
    public void Start(bool watchTitles)
    {
        Thread? old;
        lock (_lock)
        {
            if (_disposed) return;
            if (_thread != null && _watchTitles == watchTitles) return;

            old = StopLocked();
            _watchTitles = watchTitles;
            _lastForeground = GetForegroundWindow();

            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() => MessageLoop(ready)) { IsBackground = true, Name = "MasterHub-ForegroundHook" };
            _thread.Start();
            ready.Wait(2000);

            _safetyNet = new Timer(_ => CompareForeground(), null, 250, 250);
        }
        WaitFor(old);
    }

    public void Stop()
    {
        Thread? old;
        lock (_lock) { old = StopLocked(); }
        WaitFor(old);
    }

    /// <summary>Waiting for the hook thread happens outside the lock: its callbacks may call back into Start/Stop.</summary>
    private static void WaitFor(Thread? thread)
    {
        if (thread != null && thread != Thread.CurrentThread) thread.Join(1000);
    }

    private Thread? StopLocked()
    {
        _safetyNet?.Dispose();
        _safetyNet = null;

        var thread = _thread;
        if (thread != null)
        {
            if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread = null;
            _threadId = 0;
        }
        return thread;
    }

    private void MessageLoop(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        IntPtr fgHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        IntPtr nameHook = _watchTitles
            ? SetWinEventHook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _callback, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)
            : IntPtr.Zero;
        PeekMessage(out _, IntPtr.Zero, 0x0400, 0x0400, 0); // force creation of this thread's message queue before anyone posts WM_QUIT
        ready.Set();

        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (fgHook != IntPtr.Zero) UnhookWinEvent(fgHook);
            if (nameHook != IntPtr.Zero) UnhookWinEvent(nameHook);
        }
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint threadId, uint timeMs)
    {
        if (eventType == EVENT_OBJECT_NAMECHANGE)
        {
            // only the title of the window that is in front matters
            if (idObject != OBJID_WINDOW || hwnd != GetForegroundWindow()) return;
        }
        else
        {
            _lastForeground = hwnd;
        }

        Raise();
    }

    private void CompareForeground()
    {
        IntPtr current = GetForegroundWindow();
        if (current == _lastForeground) return;
        _lastForeground = current;
        Raise();
    }

    private void Raise()
    {
        try { Changed?.Invoke(); } catch { /* a handler must never kill the hook thread or the timer */ }
    }

    public void Dispose()
    {
        Thread? old;
        lock (_lock)
        {
            _disposed = true;
            old = StopLocked();
        }
        WaitFor(old);
    }

    #region Win32
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc proc,
        uint idProcess, uint idThread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    #endregion
}

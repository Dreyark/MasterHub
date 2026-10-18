using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.Teams;

/// <summary>
/// Usługa sterowania spotkaniami Microsoft Teams (Etap 5.6).
/// Obsługuje globalne skróty klawiszowe okna spotkania Teams (Ctrl+Shift+M, Ctrl+Shift+O, Ctrl+Shift+K, Ctrl+Shift+H)
/// z automatycznym wyszukiwaniem okna procesu Teams oraz przywracaniem fokusu pierwotnego okna.
/// </summary>
public class TeamsService : ITeamsService
{
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private readonly Func<TeamsShortcutType, Task<bool>>? _customShortcutHandler;
    private readonly Func<bool>? _customProcessDetector;
    private readonly SemaphoreSlim _shortcutLock = new(1, 1);
    private readonly Timer? _processCheckTimer;

    private bool _isTeamsRunning;
    private string? _detectedTeamsProcessName;
    private bool _isMuted;
    private bool _isCameraOn;
    private bool _isHandRaised;
    private bool _disposed;

    public bool IsTeamsRunning => _isTeamsRunning;
    public string? DetectedTeamsProcessName => _detectedTeamsProcessName;

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                MeetingStateChanged?.Invoke(_isMuted, _isCameraOn, _isHandRaised);
            }
        }
    }

    public bool IsCameraOn
    {
        get => _isCameraOn;
        set
        {
            if (_isCameraOn != value)
            {
                _isCameraOn = value;
                MeetingStateChanged?.Invoke(_isMuted, _isCameraOn, _isHandRaised);
            }
        }
    }

    public bool IsHandRaised
    {
        get => _isHandRaised;
        set
        {
            if (_isHandRaised != value)
            {
                _isHandRaised = value;
                MeetingStateChanged?.Invoke(_isMuted, _isCameraOn, _isHandRaised);
            }
        }
    }

    public event Action<bool>? TeamsRunningChanged;
    public event Action<string>? LogMessage;
    public event Action<bool, bool, bool>? MeetingStateChanged;

    public TeamsService(
        Func<TeamsShortcutType, Task<bool>>? customShortcutHandler = null,
        Func<bool>? customProcessDetector = null)
    {
        _customShortcutHandler = customShortcutHandler;
        _customProcessDetector = customProcessDetector;

        CheckIfTeamsRunning();

        // Okresowe sprawdzanie obecności procesu Teams w tle co 5 sekund
        _processCheckTimer = new Timer(_ =>
        {
            if (!_disposed)
            {
                CheckIfTeamsRunning();
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public bool CheckIfTeamsRunning()
    {
        if (_customProcessDetector != null)
        {
            bool customRunning = _customProcessDetector();
            UpdateRunningState(customRunning, customRunning ? "teams-mock" : null);
            return customRunning;
        }

        try
        {
            // Nowy Teams (Teams 2.0 / WebView2): ms-teams.exe
            var newTeams = Process.GetProcessesByName("ms-teams");
            if (newTeams.Length > 0)
            {
                UpdateRunningState(true, "ms-teams");
                return true;
            }

            // Klasyczny Teams (Teams 1.0 / Electron): Teams.exe
            var classicTeams = Process.GetProcessesByName("Teams");
            if (classicTeams.Length > 0)
            {
                UpdateRunningState(true, "Teams");
                return true;
            }

            // Sprawdzenie czy istnieje jakiekolwiek okno z Microsoft Teams w tytule
            IntPtr teamsWnd = FindTeamsWindowHandle();
            if (teamsWnd != IntPtr.Zero)
            {
                UpdateRunningState(true, "Microsoft Teams (Window)");
                return true;
            }

            UpdateRunningState(false, null);
            return false;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Teams] Błąd sprawdzania procesu Teams: {ex.Message}");
            return false;
        }
    }

    private void UpdateRunningState(bool running, string? processName)
    {
        bool changed = _isTeamsRunning != running || _detectedTeamsProcessName != processName;
        _isTeamsRunning = running;
        _detectedTeamsProcessName = processName;

        if (changed)
        {
            TeamsRunningChanged?.Invoke(running);
        }
    }

    public async Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        bool ok = await SendTeamsShortcutAsync(TeamsShortcutType.Mute, cancellationToken).ConfigureAwait(false);
        if (ok)
        {
            IsMuted = !IsMuted;
            LogMessage?.Invoke($"[Teams] Przełączono mikrofon: {(IsMuted ? "WYCISZONY (Muted)" : "AKTYWNY (Unmuted)")}");
        }
        return ok;
    }

    public async Task<bool> ToggleCameraAsync(CancellationToken cancellationToken = default)
    {
        bool ok = await SendTeamsShortcutAsync(TeamsShortcutType.Camera, cancellationToken).ConfigureAwait(false);
        if (ok)
        {
            IsCameraOn = !IsCameraOn;
            LogMessage?.Invoke($"[Teams] Przełączono kamerę: {(IsCameraOn ? "WŁĄCZONA (Camera On)" : "WYŁĄCZONA (Camera Off)")}");
        }
        return ok;
    }

    public async Task<bool> ToggleRaiseHandAsync(CancellationToken cancellationToken = default)
    {
        bool ok = await SendTeamsShortcutAsync(TeamsShortcutType.RaiseHand, cancellationToken).ConfigureAwait(false);
        if (ok)
        {
            IsHandRaised = !IsHandRaised;
            LogMessage?.Invoke($"[Teams] Przełączono rękę: {(IsHandRaised ? "PODNIESIONA (Hand Raised)" : "OPUSZCZONA (Hand Lowered)")}");
        }
        return ok;
    }

    public async Task<bool> LeaveMeetingAsync(CancellationToken cancellationToken = default)
    {
        bool ok = await SendTeamsShortcutAsync(TeamsShortcutType.LeaveMeeting, cancellationToken).ConfigureAwait(false);
        if (ok)
        {
            IsHandRaised = false;
            LogMessage?.Invoke("[Teams] Wysłano polecenie opuszczenia spotkania (Leave Meeting)");
        }
        return ok;
    }

    public async Task<bool> SendTeamsShortcutAsync(TeamsShortcutType shortcut, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        if (_customShortcutHandler != null)
        {
            return await _customShortcutHandler(shortcut).ConfigureAwait(false);
        }

        await _shortcutLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte virtualKey = shortcut switch
            {
                TeamsShortcutType.Mute => 0x4D,        // 'M'
                TeamsShortcutType.Camera => 0x4F,      // 'O'
                TeamsShortcutType.RaiseHand => 0x4B,   // 'K'
                TeamsShortcutType.LeaveMeeting => 0x48,// 'H'
                _ => 0x4D
            };

            string shortcutStr = shortcut switch
            {
                TeamsShortcutType.Mute => "Ctrl+Shift+M (Mute)",
                TeamsShortcutType.Camera => "Ctrl+Shift+O (Camera)",
                TeamsShortcutType.RaiseHand => "Ctrl+Shift+K (Raise Hand)",
                TeamsShortcutType.LeaveMeeting => "Ctrl+Shift+H (Leave Meeting)",
                _ => "Ctrl+Shift+Key"
            };

            IntPtr teamsWindow = FindTeamsWindowHandle();
            IntPtr previousForeground = GetForegroundWindow();

            bool switchedFocus = false;
            if (teamsWindow != IntPtr.Zero && teamsWindow != previousForeground)
            {
                SetForegroundWindow(teamsWindow);
                switchedFocus = true;
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            }

            // Symulacja naciśnięcia Ctrl + Shift + MainKey
            PressShortcut(virtualKey);

            if (switchedFocus && previousForeground != IntPtr.Zero)
            {
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                SetForegroundWindow(previousForeground);
            }

            LogMessage?.Invoke($"[Teams] Wysłano skrót {shortcutStr} do okna Teams.");
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Teams] Błąd wysyłania skrótu {shortcut}: {ex.Message}");
            return false;
        }
        finally
        {
            _shortcutLock.Release();
        }
    }

    private static void PressShortcut(byte mainKey)
    {
        // 1. Wciśnięcie klawiszy modyfikujących: Ctrl, Shift
        keybd_event(VK_CONTROL, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);

        // 2. Wciśnięcie i zwolnienie klawisza głównego
        keybd_event(mainKey, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(mainKey, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);

        // 3. Zwolnienie modyfikatorów
        keybd_event(VK_SHIFT, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static IntPtr FindTeamsWindowHandle()
    {
        IntPtr meetingHandle = IntPtr.Zero;
        IntPtr mainHandle = IntPtr.Zero;

        try
        {
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                if (string.IsNullOrWhiteSpace(title)) return true;

                // Okno spotkania Teams zazwyczaj zawiera słowo "Meeting" lub "Spotkanie"
                if (title.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Spotkanie", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Rozmowa", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Call", StringComparison.OrdinalIgnoreCase))
                {
                    if (title.Contains("Teams", StringComparison.OrdinalIgnoreCase))
                    {
                        meetingHandle = hWnd;
                        return false; // Znaleziono idealne okno spotkania
                    }
                }

                // Główne okno Teams jako fallback
                if (title.EndsWith("Microsoft Teams", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Microsoft Teams", StringComparison.OrdinalIgnoreCase))
                {
                    if (mainHandle == IntPtr.Zero)
                    {
                        mainHandle = hWnd;
                    }
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Fallback na wypadek ograniczeń środowiska
        }

        if (meetingHandle != IntPtr.Zero) return meetingHandle;
        if (mainHandle != IntPtr.Zero) return mainHandle;

        // Fallback: MainWindowHandle z procesów ms-teams lub Teams
        try
        {
            foreach (var proc in Process.GetProcessesByName("ms-teams"))
            {
                if (proc.MainWindowHandle != IntPtr.Zero) return proc.MainWindowHandle;
            }
            foreach (var proc in Process.GetProcessesByName("Teams"))
            {
                if (proc.MainWindowHandle != IntPtr.Zero) return proc.MainWindowHandle;
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    #region Win32 P/Invoke
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _processCheckTimer?.Dispose();
        _shortcutLock.Dispose();
    }
}

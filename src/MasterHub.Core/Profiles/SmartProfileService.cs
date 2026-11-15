using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MasterHub.Core.Profiles;

/// <summary>
/// Smart Profiles (T-01): watches the foreground window and asks the app to switch profile according to the rules
/// stored in the profiles. Event-driven (<see cref="ForegroundWatcher"/>): <b>nothing runs while there is no enabled
/// rule or while auto-switching is off</b>. When no rule matches any more, the service returns to
/// <see cref="FallbackProfileId"/> – the app keeps that at the <i>last profile the user picked</i> (not necessarily Home).
/// </summary>
public sealed class SmartProfileService : ISmartProfileService
{
    private readonly object _syncRoot = new();
    private readonly List<SmartProfileRule> _rules = new();
    private readonly ForegroundWatcher _watcher = new();
    private bool _isEnabled = true;
    private bool _started;
    private int _pollingIntervalMs = 250;
    private string _lastMatchedProfileId = "";
    private string _lastForegroundProcess = "";
    private string _lastForegroundTitle = "";
    private string? _fallbackProfileId;
    private bool _disposed;
    private int _evaluating;
    private int _again;   // a change arrived while an evaluation was running: look once more when it ends

    public bool IsEnabled
    {
        get { lock (_syncRoot) { return _isEnabled; } }
        set
        {
            bool changed;
            lock (_syncRoot)
            {
                changed = _isEnabled != value;
                _isEnabled = value;
                if (!value) _lastMatchedProfileId = "";
            }

            RefreshWatching();
            if (changed) IsEnabledChanged?.Invoke(value);
        }
    }

    /// <summary>Kept for compatibility: the interval of the safety-net comparison is fixed at 250 ms.</summary>
    public int PollingIntervalMs
    {
        get { lock (_syncRoot) { return _pollingIntervalMs; } }
        set { lock (_syncRoot) { _pollingIntervalMs = Math.Max(100, value); } }
    }

    public string? FallbackProfileId
    {
        get { lock (_syncRoot) { return _fallbackProfileId; } }
        set { lock (_syncRoot) { _fallbackProfileId = value; } }
    }

    public string LastForegroundProcess
    {
        get { lock (_syncRoot) { return _lastForegroundProcess; } }
    }

    public string LastForegroundTitle
    {
        get { lock (_syncRoot) { return _lastForegroundTitle; } }
    }

    /// <summary>True while the hook/timer are actually running (only with ≥ 1 enabled rule and auto-switch on).</summary>
    public bool IsWatching => _watcher.IsRunning;

    public event Action<string, string>? ForegroundWindowChanged;
    public event Action<string>? AutoSwitchProfileRequested;
    public event Action<bool>? IsEnabledChanged;

    public SmartProfileService()
    {
        _watcher.Changed += EvaluateForegroundWindow;
        Start();
    }

    public void UpdateRules(IEnumerable<SmartProfileRule> rules)
    {
        lock (_syncRoot)
        {
            _rules.Clear();
            if (rules != null) _rules.AddRange(rules);
            // _lastMatchedProfileId is kept: the active profile is still the one a rule switched to, so leaving
            // that window must still return to the fallback even if the rules were just rebuilt (e.g. a profile was saved).
        }

        RefreshWatching();
    }

    public void Start()
    {
        lock (_syncRoot) { _started = true; }
        RefreshWatching();
    }

    public void Stop()
    {
        lock (_syncRoot) { _started = false; }
        RefreshWatching();
    }

    public void CheckForegroundWindowNow() => EvaluateForegroundWindow();

    /// <summary>Starts or stops the hook so that it only runs when it can change anything.</summary>
    private void RefreshWatching()
    {
        bool needed;
        bool titles;
        lock (_syncRoot)
        {
            var active = _rules.Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.ProcessName)).ToList();
            needed = _started && _isEnabled && !_disposed && active.Count > 0;
            titles = active.Any(r => !string.IsNullOrWhiteSpace(r.WindowTitle));
        }

        if (needed) _watcher.Start(titles);
        else _watcher.Stop();
    }

    /// <summary>
    /// The rule that applies to a window. Rules that also name a window-title fragment are more specific, so they win over
    /// process-only rules; within each group the order of <paramref name="rules"/> decides.
    /// </summary>
    public static SmartProfileRule? FindMatchingRule(IEnumerable<SmartProfileRule> rules, string processName, string windowTitle)
    {
        var matching = rules.Where(r => r.Matches(processName, windowTitle)).ToList();
        return matching.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.WindowTitle)) ?? matching.FirstOrDefault();
    }

    private void EvaluateForegroundWindow()
    {
        // The hook and the safety-net timer can fire together: never evaluate twice in parallel, but never lose a change either.
        if (Interlocked.CompareExchange(ref _evaluating, 1, 0) != 0)
        {
            Volatile.Write(ref _again, 1);
            return;
        }
        do
        {
            Volatile.Write(ref _again, 0);
            EvaluateOnce();
            Interlocked.Exchange(ref _evaluating, 0);
            // a change that came in after the last look and before the guard was released
        } while (Volatile.Read(ref _again) == 1 && Interlocked.CompareExchange(ref _evaluating, 1, 0) == 0);
    }

    private void EvaluateOnce()
    {
        try
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0) return;

            // MasterHub's own window (editing a game profile) must not trigger a switch back (F-10).
            if (processId == (uint)Environment.ProcessId) return;

            string processName;
            try
            {
                using var proc = Process.GetProcessById((int)processId);
                processName = proc.ProcessName;
            }
            catch
            {
                return;
            }

            var sbTitle = new StringBuilder(512);
            GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
            ApplyForeground(processName, sbTitle.ToString());
        }
        catch
        {
            // P/Invoke or process lookup must never kill the hook thread / timer
        }
    }

    /// <summary>The decision logic, free of Win32 so it can be tested: a window came to the front.</summary>
    internal void ApplyForeground(string processName, string windowTitle)
    {
        bool changed = false;
        lock (_syncRoot)
        {
            if (!_isEnabled || _disposed) return;

            if (!string.Equals(_lastForegroundProcess, processName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_lastForegroundTitle, windowTitle, StringComparison.Ordinal))
            {
                _lastForegroundProcess = processName;
                _lastForegroundTitle = windowTitle;
                changed = true;
            }
        }

        if (!changed) return;

        ForegroundWindowChanged?.Invoke(processName, windowTitle);

        SmartProfileRule? matchingRule;
        lock (_syncRoot) { matchingRule = FindMatchingRule(_rules, processName, windowTitle); }

        if (matchingRule != null)
        {
            string target = matchingRule.ProfileId;
            bool shouldSwitch;
            lock (_syncRoot)
            {
                shouldSwitch = _lastMatchedProfileId != target;
                if (shouldSwitch) _lastMatchedProfileId = target;
            }

            if (shouldSwitch) AutoSwitchProfileRequested?.Invoke(target);
            return;
        }

        string? restore = null;
        lock (_syncRoot)
        {
            if (!string.IsNullOrEmpty(_lastMatchedProfileId))
            {
                _lastMatchedProfileId = "";
                restore = _fallbackProfileId;
            }
        }

        if (!string.IsNullOrEmpty(restore)) AutoSwitchProfileRequested?.Invoke(restore);
    }

    public void Dispose()
    {
        lock (_syncRoot) { _disposed = true; }
        _watcher.Dispose();
    }

    #region Win32 P/Invoke
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    #endregion
}

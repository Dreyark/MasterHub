using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MasterHub.Core.Profiles;

/// <summary>
/// Implementacja Smart Profile Service oparta o natywne Win32 API.
/// Monitoruje aktywne okno, automatycznie przełącza profile MasterHub wg reguł (Etap 2.2),
/// a po utracie fokusa powraca do profilu bazowego (FallbackProfileId).
/// </summary>
public sealed class SmartProfileService : ISmartProfileService
{
    private readonly object _syncRoot = new();
    private readonly List<SmartProfileRule> _rules = new();
    private Timer? _pollingTimer;
    private bool _isEnabled = true;
    private int _pollingIntervalMs = 500;
    private string _lastMatchedProfileId = "";
    private string _lastForegroundProcess = "";
    private string _lastForegroundTitle = "";
    private string? _fallbackProfileId;
    private bool _disposed;

    public bool IsEnabled
    {
        get
        {
            lock (_syncRoot)
            {
                return _isEnabled;
            }
        }
        set
        {
            lock (_syncRoot)
            {
                _isEnabled = value;
                if (!value)
                {
                    _lastMatchedProfileId = "";
                }
            }
        }
    }

    public int PollingIntervalMs
    {
        get
        {
            lock (_syncRoot)
            {
                return _pollingIntervalMs;
            }
        }
        set
        {
            lock (_syncRoot)
            {
                _pollingIntervalMs = Math.Max(100, value);
                _pollingTimer?.Change(_pollingIntervalMs, _pollingIntervalMs);
            }
        }
    }

    public string? FallbackProfileId
    {
        get
        {
            lock (_syncRoot)
            {
                return _fallbackProfileId;
            }
        }
        set
        {
            lock (_syncRoot)
            {
                _fallbackProfileId = value;
            }
        }
    }

    public string LastForegroundProcess
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastForegroundProcess;
            }
        }
    }

    public string LastForegroundTitle
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastForegroundTitle;
            }
        }
    }

    public event Action<string, string>? ForegroundWindowChanged;
    public event Action<string>? AutoSwitchProfileRequested;

    public SmartProfileService()
    {
        Start();
    }

    public void UpdateRules(IEnumerable<SmartProfileRule> rules)
    {
        lock (_syncRoot)
        {
            _rules.Clear();
            if (rules != null)
            {
                _rules.AddRange(rules);
            }
        }
    }

    public void Start()
    {
        lock (_syncRoot)
        {
            if (_pollingTimer == null && !_disposed)
            {
                _pollingTimer = new Timer(OnTimerTick, null, 250, _pollingIntervalMs);
            }
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            _pollingTimer?.Dispose();
            _pollingTimer = null;
        }
    }

    public void CheckForegroundWindowNow()
    {
        EvaluateForegroundWindow();
    }

    private void OnTimerTick(object? state)
    {
        lock (_syncRoot)
        {
            if (!_isEnabled || _disposed) return;
        }
        EvaluateForegroundWindow();
    }

    private void EvaluateForegroundWindow()
    {
        try
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0) return;

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
            string windowTitle = sbTitle.ToString();

            bool changed = false;
            lock (_syncRoot)
            {
                if (!string.Equals(_lastForegroundProcess, processName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_lastForegroundTitle, windowTitle, StringComparison.Ordinal))
                {
                    _lastForegroundProcess = processName;
                    _lastForegroundTitle = windowTitle;
                    changed = true;
                }
            }

            if (changed)
            {
                ForegroundWindowChanged?.Invoke(processName, windowTitle);

                SmartProfileRule? matchingRule = null;
                lock (_syncRoot)
                {
                    foreach (var rule in _rules)
                    {
                        if (rule.Matches(processName, windowTitle))
                        {
                            matchingRule = rule;
                            break;
                        }
                    }
                }

                if (matchingRule != null)
                {
                    string targetProfileId = matchingRule.ProfileId;
                    bool shouldSwitch = false;
                    lock (_syncRoot)
                    {
                        if (_lastMatchedProfileId != targetProfileId)
                        {
                            _lastMatchedProfileId = targetProfileId;
                            shouldSwitch = true;
                        }
                    }

                    if (shouldSwitch)
                    {
                        AutoSwitchProfileRequested?.Invoke(targetProfileId);
                    }
                }
                else
                {
                    string? restoreProfileId = null;
                    lock (_syncRoot)
                    {
                        if (!string.IsNullOrEmpty(_lastMatchedProfileId))
                        {
                            _lastMatchedProfileId = "";
                            restoreProfileId = _fallbackProfileId;
                        }
                    }

                    if (!string.IsNullOrEmpty(restoreProfileId))
                    {
                        AutoSwitchProfileRequested?.Invoke(restoreProfileId);
                    }
                }
            }
        }
        catch
        {
            // P/Invoke lub odczyt procesu nie może przerwać działania timera
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Stop();
            }
            _disposed = true;
        }
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

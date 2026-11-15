using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace MasterHub.Core.Profiles.Auto;

/// <summary>How long a program was in front, and on how many days (T-37). Seconds are capped per stretch, see <see cref="AppUsageService.MaxStretch"/>.</summary>
public sealed class AppUsageEntry
{
    public long Seconds { get; set; }
    public int Days { get; set; }

    /// <summary>The last day (<c>yyyy-MM-dd</c>) the program was counted, so <see cref="Days"/> grows once a day.</summary>
    public string LastDay { get; set; } = "";
}

/// <summary>The usage file. It records <i>since when</i> it counts, so the numbers can be read honestly.</summary>
public sealed class AppUsageData
{
    public DateTime Since { get; set; }
    public Dictionary<string, AppUsageEntry> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Which programs the user really works in (T-37): the seconds each program is in front, how many days, and when it was last used.
/// Local file only, never sent anywhere. Runs only while it is started (the assistant is on), and does nothing else.
/// </summary>
public interface IAppUsageService : IDisposable
{
    bool IsRunning { get; }

    /// <summary>Since when the numbers count.</summary>
    DateTime Since { get; }

    /// <summary>Raised on a background thread when another program came to the front (its process name).</summary>
    event Action<string>? AppInFront;

    /// <summary>A copy of the numbers, by process name.</summary>
    IReadOnlyDictionary<string, AppUsageEntry> Snapshot();

    void Start();

    /// <summary>Stops looking and saves what was counted.</summary>
    void Stop();

    /// <summary>Forgets everything counted so far (Settings: "Delete counted data").</summary>
    void Clear();
}

/// <inheritdoc cref="IAppUsageService"/>
public sealed class AppUsageService : IAppUsageService
{
    /// <summary>How often the program in front is looked at.</summary>
    public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(5);

    /// <summary>
    /// One stretch in front counts at most this long: a program left in front over lunch is not work. The stretch starts again
    /// when another program comes to the front.
    /// </summary>
    public static readonly TimeSpan MaxStretch = TimeSpan.FromMinutes(15);

    /// <summary>The file is written at most this often (and when counting stops).</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(2);

    /// <summary>A gap longer than this between two looks (sleep, a busy machine) is not counted as time in front.</summary>
    private static readonly double MaxGapSeconds = SampleEvery.TotalSeconds * 3;

    private readonly string _path;
    private readonly Func<string?> _foreground;
    private readonly Func<DateTime> _now;
    private readonly bool _useTimer;
    private readonly object _lock = new();
    private AppUsageData _data = new();
    private Timer? _timer;
    private string? _previous;
    private DateTime? _last;
    private double _stretch;
    private DateTime _lastSave = DateTime.MinValue;
    private bool _dirty;
    private bool _disposed;

    public AppUsageService(string? path = null, Func<string?>? foreground = null, Func<DateTime>? now = null, bool useTimer = true)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "usage.json");
        _foreground = foreground ?? ForegroundProbe.CurrentProcessName;
        _now = now ?? (() => DateTime.Now);
        _useTimer = useTimer;
        _data = Load() ?? new AppUsageData { Since = _now() };
    }

    public event Action<string>? AppInFront;

    public bool IsRunning
    {
        get { lock (_lock) return _timer != null || _runningWithoutTimer; }
    }

    private bool _runningWithoutTimer;

    public DateTime Since
    {
        get { lock (_lock) return _data.Since; }
    }

    public IReadOnlyDictionary<string, AppUsageEntry> Snapshot()
    {
        lock (_lock)
        {
            return _data.Apps.ToDictionary(
                kv => kv.Key,
                kv => new AppUsageEntry { Seconds = kv.Value.Seconds, Days = kv.Value.Days, LastDay = kv.Value.LastDay },
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || IsRunningLocked) return;

            _previous = null;
            _last = null;
            _stretch = 0;

            if (_useTimer) _timer = new Timer(_ => SafeSample(), null, SampleEvery, SampleEvery);
            else _runningWithoutTimer = true;
        }
    }

    private bool IsRunningLocked => _timer != null || _runningWithoutTimer;

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _runningWithoutTimer = false;
            _previous = null;
            _last = null;
            SaveLocked(force: true);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _data = new AppUsageData { Since = _now() };
            _stretch = 0;
            _dirty = true;
            SaveLocked(force: true);
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_lock) { _disposed = true; }
    }

    private void SafeSample()
    {
        try { Sample(); }
        catch { /* looking at the foreground window must never take the app down */ }
    }

    /// <summary>One look at the program in front. Public to the tests, which drive it with a fake clock instead of waiting.</summary>
    internal void Sample()
    {
        string? current = _foreground();
        DateTime now = _now();
        string? raise = null;

        lock (_lock)
        {
            if (!IsRunningLocked) return;

            if (_last != null && _previous != null)
            {
                double gap = (now - _last.Value).TotalSeconds;
                if (gap > 0 && gap <= MaxGapSeconds) Credit(_previous, gap, now);
                else if (gap > MaxGapSeconds) _stretch = 0;   // the machine slept or stalled: a new stretch
            }

            if (!string.Equals(current, _previous, StringComparison.OrdinalIgnoreCase))
            {
                _stretch = 0;
                if (!string.IsNullOrEmpty(current)) raise = current;
            }

            _previous = current;
            _last = now;
            SaveLocked(force: false);
        }

        if (raise != null) AppInFront?.Invoke(raise);
    }

    private void Credit(string app, double seconds, DateTime now)
    {
        double room = MaxStretch.TotalSeconds - _stretch;
        if (room <= 0) return;

        double take = Math.Min(seconds, room);
        _stretch += take;

        if (!_data.Apps.TryGetValue(app, out var entry))
        {
            entry = new AppUsageEntry();
            _data.Apps[app] = entry;
        }

        entry.Seconds += (long)Math.Round(take);

        string day = now.ToString("yyyy-MM-dd");
        if (entry.LastDay != day)
        {
            entry.LastDay = day;
            entry.Days++;
        }

        _dirty = true;
    }

    // ── file ────────────────────────────────────────────────────────────────────────────────

    private AppUsageData? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var data = JsonSerializer.Deserialize<AppUsageData>(File.ReadAllText(_path));
            if (data == null) return null;
            data.Apps = new Dictionary<string, AppUsageEntry>(data.Apps ?? new(), StringComparer.OrdinalIgnoreCase);
            return data;
        }
        catch
        {
            return null;   // a counter file that cannot be read just starts again
        }
    }

    private void SaveLocked(bool force)
    {
        if (!_dirty) return;
        var now = _now();
        if (!force && now - _lastSave < SaveEvery && now >= _lastSave) return;

        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = $"{_path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
            _lastSave = now;
            _dirty = false;
        }
        catch
        {
            // not saved this time; it is tried again at the next look
        }
    }
}

/// <summary>Which program is in front right now (its process name without <c>.exe</c>).</summary>
internal static class ForegroundProbe
{
    /// <summary>The foreground program, or <c>null</c> when there is none, or it is MasterHub itself (editing a deck is not "using" a program).</summary>
    public static string? CurrentProcessName()
    {
        try
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return null;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) return null;

            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

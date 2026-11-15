using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MasterHub.Core.Audio;
using MasterHub.Core.Looks;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// Feeds the conditional looks (T-12) from the monitor, now-playing, audio and foreground services.
/// Each value is read on demand, and the slow ones (audio endpoints, foreground process) are throttled, so a key without rules costs nothing.
/// </summary>
public sealed class LookSignals : ILookSignals
{
    private static readonly TimeSpan AudioInterval = TimeSpan.FromMilliseconds(1500);

    private readonly ISystemMonitorService _monitor;
    private readonly INowPlayingService _nowPlaying;
    private readonly IAudioService? _audio;
    private readonly Func<string?> _foreground;
    private readonly Func<DateTime> _clock;
    private readonly MasterHub.Core.Status.IStatusBoard? _status;

    private DateTime _audioReadAt = DateTime.MinValue;
    private bool _micMuted, _masterMuted;

    public LookSignals(ISystemMonitorService monitor, INowPlayingService nowPlaying, IAudioService? audio,
        Func<string?>? foreground = null, Func<DateTime>? clock = null, MasterHub.Core.Status.IStatusBoard? status = null)
    {
        _status = status;
        _monitor = monitor;
        _nowPlaying = nowPlaying;
        _audio = audio;
        _foreground = foreground ?? ForegroundProcess.CurrentName;
        _clock = clock ?? (() => DateTime.Now);
    }

    public double Cpu => _monitor.CpuUsagePercent;
    public double Ram => _monitor.RamUsagePercent;
    public double Gpu => _monitor.GpuUsagePercent;

    // no CPU sensor → the GPU temperature stands in for it (the same fallback the temperature key uses)
    public double CpuTemp => _monitor.CpuTemperatureCelsius > 0 ? _monitor.CpuTemperatureCelsius : _monitor.GpuTemperatureCelsius;
    public double GpuTemp => _monitor.GpuTemperatureCelsius;

    public bool MusicPlaying => _nowPlaying.IsPlaying;
    public string? Song => _nowPlaying.CurrentTitle;
    public string? Artist => _nowPlaying.CurrentArtist;

    public bool MicMuted { get { SampleAudio(); return _micMuted; } }
    public bool MasterMuted { get { SampleAudio(); return _masterMuted; } }

    // what the connected apps report (T-25): the board is in memory, so reading it is free
    public int StatusWaiting => _status?.Count(MasterHub.Core.Status.AppState.Waiting) ?? 0;
    public int StatusWorking => _status?.Count(MasterHub.Core.Status.AppState.Working) ?? 0;
    public int StatusDone => _status?.Count(MasterHub.Core.Status.AppState.Done) ?? 0;
    public int StatusError => _status?.Count(MasterHub.Core.Status.AppState.Error) ?? 0;

    // Items come most urgent first; idle and offline items are not "news" for a key, so they do not count as the top one.
    private MasterHub.Core.Status.StatusItem? Top => _status?.Items.FirstOrDefault(i => i.State is not (MasterHub.Core.Status.AppState.Idle or MasterHub.Core.Status.AppState.Offline));
    public string? StatusTop => Top?.State.ToString().ToLowerInvariant();
    public string? StatusTopLabel => Top?.Label ?? Top?.Source;

    public string? ForegroundApp => _foreground();
    public DateTime Now => _clock();

    private void SampleAudio()
    {
        if (_audio == null) return;

        var now = _clock();
        if (now - _audioReadAt < AudioInterval && now >= _audioReadAt) return;
        _audioReadAt = now;

        try { _micMuted = _audio.IsMasterRecordingMuted(); } catch { /* no recording device */ }
        try { _masterMuted = _audio.IsMasterPlaybackMuted(); } catch { /* no playback device */ }
    }
}

/// <summary>Name (without <c>.exe</c>) of the program that owns the foreground window. The last answer is cached by process id.</summary>
internal static class ForegroundProcess
{
    private static int _lastPid;
    private static string? _lastName;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static string? CurrentName()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;
            if ((int)pid == _lastPid) return _lastName;

            using var process = Process.GetProcessById((int)pid);
            _lastPid = (int)pid;
            _lastName = process.ProcessName;
            return _lastName;
        }
        catch
        {
            return null;   // a process that closed in the meantime or is not readable
        }
    }
}

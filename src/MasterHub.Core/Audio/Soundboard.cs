using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MasterHub.Core.Audio;

/// <summary>What a second press on a sound key does.</summary>
public enum SoundPlayMode
{
    /// <summary>A press on a playing sound stops it; otherwise it starts.</summary>
    PlayStop = 0,
    /// <summary>A press starts the sound from the beginning (a copy that is still playing is cut).</summary>
    Restart = 1,
    /// <summary>A press starts one more copy over the one that is playing.</summary>
    Overlap = 2
}

/// <summary>A sound key's set-up (saved with the key). Sound files are never packed into <c>.mhdeck</c> packages.</summary>
public class SoundSettings
{
    public const int MaxVolumePercent = 200;

    public string FilePath { get; set; } = "";

    /// <summary>0–200 %. Above 100 the sound is louder than the file (a quiet recording can be lifted; very loud ones may distort).</summary>
    public int VolumePercent { get; set; } = 100;

    public SoundPlayMode Mode { get; set; } = SoundPlayMode.PlayStop;

    /// <summary>Playback device (a virtual cable for Discord / OBS, say). Empty = the Windows default device.</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>The device name, kept so the device can be found again when its id changed.</summary>
    public string DeviceName { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(FilePath);
    public bool UsesDefaultDevice => string.IsNullOrEmpty(DeviceId) && string.IsNullOrWhiteSpace(DeviceName);

    public SoundSettings Normalize()
    {
        VolumePercent = Math.Clamp(VolumePercent, 0, MaxVolumePercent);
        FilePath ??= "";
        DeviceId ??= "";
        DeviceName ??= "";
        return this;
    }

    /// <summary>0.0–2.0, what the player multiplies the samples with.</summary>
    public float Gain => Math.Clamp(VolumePercent, 0, MaxVolumePercent) / 100f;
}

public enum SoundProblem
{
    None = 0,
    /// <summary>The key has no file yet.</summary>
    NoFile,
    /// <summary>The extension is not one the player can read.</summary>
    UnsupportedType,
    FileMissing,
    /// <summary>The chosen output device is not connected.</summary>
    DeviceMissing,
    /// <summary>The file could not be decoded or the device refused to play.</summary>
    PlaybackFailed
}

public enum SoundOutcome
{
    Started,
    /// <summary>A press stopped a playing sound (<see cref="SoundPlayMode.PlayStop"/>).</summary>
    Stopped,
    Failed
}

public readonly record struct SoundResult(SoundOutcome Outcome, SoundProblem Problem = SoundProblem.None, string Detail = "")
{
    public bool IsFailure => Outcome == SoundOutcome.Failed;
    public static SoundResult Fail(SoundProblem problem, string detail = "") => new(SoundOutcome.Failed, problem, detail);
}

/// <summary>Thrown by an output that cannot play a file.</summary>
public sealed class SoundException : Exception
{
    public SoundException(SoundProblem problem, string message, Exception? inner = null) : base(message, inner) => Problem = problem;
    public SoundProblem Problem { get; }
}

public static class SoundFiles
{
    /// <summary>What Media Foundation reads on Windows 10+. OGG is not on the list: it needs a decoder of its own (a licence decision).</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = new[] { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac" };

    /// <summary>Checks a file before anything is started (empty, type, exists) so the reason can be shown to the user.</summary>
    public static SoundProblem Check(string? path, Func<string, bool>? exists = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return SoundProblem.NoFile;

        string ext = Path.GetExtension(path.Trim()).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return SoundProblem.UnsupportedType;

        return (exists ?? File.Exists)(path.Trim()) ? SoundProblem.None : SoundProblem.FileMissing;
    }
}

/// <summary>One sound that is playing.</summary>
public interface ISoundVoice
{
    bool IsPlaying { get; }
    void Stop();

    /// <summary>Raised once when the sound ended or was stopped (from the audio thread).</summary>
    event Action? Finished;
}

/// <summary>An open output stream on one device. It is kept for 30 s after the last sound, then closed.</summary>
public interface ISoundDevice : IDisposable
{
    /// <summary>Starts a file. Throws <see cref="SoundException"/> when it cannot be played.</summary>
    ISoundVoice Start(string file, float gain);
}

public interface ISoundOutputFactory
{
    /// <summary>Opens the default device (<paramref name="deviceId"/> empty) or the endpoint with that id.</summary>
    ISoundDevice Open(string deviceId);
}

public interface ISoundboardService : IDisposable
{
    /// <summary>Plays, restarts or stops a sound according to its mode.</summary>
    SoundResult Play(SoundSettings settings);

    void StopAll();

    /// <summary>Whether the sound of these settings is playing (on the device they name).</summary>
    bool IsPlaying(SoundSettings settings);

    int ActiveVoices { get; }

    /// <summary>A sound started, stopped or ended.</summary>
    event Action? PlaybackChanged;
}

/// <summary>
/// The sound board (T-14). Which sound is playing is told apart by <c>device|file</c>, so the same file on two devices
/// is two sounds. Nothing is opened until the first sound; an output that has been silent for 30 s is closed again, so an
/// idle app holds no audio device.
/// </summary>
public sealed class SoundboardService : ISoundboardService
{
    public static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(30);

    private sealed class Voice
    {
        public required ISoundVoice Handle;
        public required string Key;
        public required string DeviceId;
    }

    private sealed class OpenDevice
    {
        public required ISoundDevice Device;
        public DateTime LastActive;
    }

    private readonly object _gate = new();
    private readonly ISoundOutputFactory _factory;
    private readonly IAudioDeviceService? _devices;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, bool> _fileExists;
    private readonly List<Voice> _voices = new();
    private readonly Dictionary<string, OpenDevice> _open = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _idleTimer;
    private bool _disposed;

    public SoundboardService(ISoundOutputFactory factory, IAudioDeviceService? devices = null, Func<DateTime>? clock = null,
        Func<string, bool>? fileExists = null, bool autoClose = true)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _devices = devices;
        _clock = clock ?? (() => DateTime.UtcNow);
        _fileExists = fileExists ?? File.Exists;
        _autoClose = autoClose;
    }

    private readonly bool _autoClose;

    public event Action? PlaybackChanged;

    public int ActiveVoices
    {
        get { lock (_gate) { Prune(); return _voices.Count; } }
    }

    /// <summary>Devices that are open right now (tests; the idle close).</summary>
    public int OpenDevices
    {
        get { lock (_gate) return _open.Count; }
    }

    public SoundResult Play(SoundSettings settings)
    {
        settings = (settings ?? new SoundSettings()).Normalize();

        var problem = SoundFiles.Check(settings.FilePath, _fileExists);
        if (problem != SoundProblem.None) return SoundResult.Fail(problem, settings.FilePath);

        if (!TryResolveDevice(settings, out string deviceId)) return SoundResult.Fail(SoundProblem.DeviceMissing, settings.DeviceName);

        string file = settings.FilePath.Trim();
        string key = KeyOf(deviceId, file);
        SoundResult result;

        lock (_gate)
        {
            if (_disposed) return SoundResult.Fail(SoundProblem.PlaybackFailed, "stopped");
            Prune();

            var same = _voices.Where(v => v.Key == key).ToList();
            if (same.Count > 0 && settings.Mode == SoundPlayMode.PlayStop)
            {
                foreach (var v in same) v.Handle.Stop();
                _voices.RemoveAll(v => same.Contains(v));
                Touch(deviceId);
                result = new SoundResult(SoundOutcome.Stopped);
            }
            else
            {
                if (settings.Mode == SoundPlayMode.Restart)
                {
                    foreach (var v in same) v.Handle.Stop();
                    _voices.RemoveAll(v => same.Contains(v));
                }

                try
                {
                    var device = GetOrOpen(deviceId);
                    var handle = device.Device.Start(file, settings.Gain);
                    var voice = new Voice { Handle = handle, Key = key, DeviceId = deviceId };
                    _voices.Add(voice);
                    handle.Finished += () => OnVoiceFinished(voice);
                    Touch(deviceId);
                    result = new SoundResult(SoundOutcome.Started);
                }
                catch (SoundException ex)
                {
                    return SoundResult.Fail(ex.Problem, ex.Message);
                }
                catch (Exception ex)
                {
                    return SoundResult.Fail(SoundProblem.PlaybackFailed, ex.Message);
                }
            }
        }

        PlaybackChanged?.Invoke();
        return result;
    }

    public void StopAll()
    {
        bool any;
        lock (_gate)
        {
            any = _voices.Count > 0;
            foreach (var v in _voices.ToList()) v.Handle.Stop();   // a voice may report its end from inside Stop()
            _voices.Clear();
            foreach (var d in _open.Values) d.LastActive = _clock();
        }

        if (any) PlaybackChanged?.Invoke();
    }

    public bool IsPlaying(SoundSettings settings)
    {
        settings = (settings ?? new SoundSettings()).Normalize();
        if (!settings.IsConfigured || !TryResolveDevice(settings, out string deviceId)) return false;

        string key = KeyOf(deviceId, settings.FilePath.Trim());
        lock (_gate)
        {
            Prune();
            return _voices.Any(v => v.Key == key);
        }
    }

    /// <summary>Closes the outputs that have been silent for 30 s. Returns how many were closed.</summary>
    public int CloseIdle()
    {
        var closed = new List<ISoundDevice>();
        lock (_gate)
        {
            Prune();
            var now = _clock();
            foreach (var (id, device) in _open.ToList())
            {
                bool busy = _voices.Any(v => string.Equals(v.DeviceId, id, StringComparison.OrdinalIgnoreCase));
                if (busy || now - device.LastActive < IdleClose) continue;

                _open.Remove(id);
                closed.Add(device.Device);
            }

            if (_open.Count == 0) StopIdleTimer();
        }

        foreach (var device in closed) SafeDispose(device);
        return closed.Count;
    }

    public void Dispose()
    {
        List<ISoundDevice> devices;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var v in _voices.ToList()) v.Handle.Stop();
            _voices.Clear();
            devices = _open.Values.Select(d => d.Device).ToList();
            _open.Clear();
            StopIdleTimer();
        }

        foreach (var device in devices) SafeDispose(device);
    }

    // ── internals ───────────────────────────────────────────────────────────────────────────

    private static string KeyOf(string deviceId, string file) => (deviceId + "|" + file).ToLowerInvariant();

    /// <summary>The saved device by id, then by name; an empty choice means the default device ("").</summary>
    private bool TryResolveDevice(SoundSettings settings, out string deviceId)
    {
        deviceId = "";
        if (settings.UsesDefaultDevice) return true;
        if (_devices == null) { deviceId = settings.DeviceId; return true; }   // nothing to check against: trust the id

        var found = AudioDeviceMatcher.Resolve(_devices.GetDevices(capture: false), new SavedAudioDevice(settings.DeviceId, settings.DeviceName));
        if (found == null) return false;

        deviceId = found.Id;
        return true;
    }

    private OpenDevice GetOrOpen(string deviceId)
    {
        if (_open.TryGetValue(deviceId, out var existing)) return existing;

        var opened = new OpenDevice { Device = _factory.Open(deviceId), LastActive = _clock() };
        _open[deviceId] = opened;
        if (_autoClose) StartIdleTimer();
        return opened;
    }

    private void Touch(string deviceId)
    {
        if (_open.TryGetValue(deviceId, out var d)) d.LastActive = _clock();
    }

    private void Prune() => _voices.RemoveAll(v => !v.Handle.IsPlaying);

    private void OnVoiceFinished(Voice voice)
    {
        lock (_gate)
        {
            _voices.Remove(voice);
            Touch(voice.DeviceId);
        }

        PlaybackChanged?.Invoke();
    }

    // one timer for every device, started with the first output and stopped when the last one is closed
    private void StartIdleTimer() => _idleTimer ??= new Timer(_ => CloseIdle(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    private void StopIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    private static void SafeDispose(IDisposable d)
    {
        try { d.Dispose(); } catch { /* a device that went away */ }
    }
}

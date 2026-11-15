using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MasterHub.Core.Profiles.Auto;

/// <summary>What is on disk: presses per profile and control, and since when they are counted.</summary>
public sealed class PressData
{
    public DateTime Since { get; set; }
    public Dictionary<string, Dictionary<string, long>> Profiles { get; set; } = new();
}

/// <summary>
/// Counts presses per control (T-37) so a layout can follow what is really used. <b>Off by default and local only</b>: nothing is
/// counted unless the user turned it on, the file never leaves this PC, and "Delete counted data" removes it.
/// </summary>
public interface IPressCounter
{
    /// <summary>True while counting is on.</summary>
    bool IsActive { get; }

    /// <summary>Since when the numbers count (<c>null</c> when nothing was counted).</summary>
    DateTime? Since { get; }

    /// <summary>Counts one press of a control. Does nothing while counting is off.</summary>
    void Count(string profileId, string controlId);

    /// <summary>Presses per control of a profile (a copy).</summary>
    IReadOnlyDictionary<string, long> Get(string profileId);

    /// <summary>Writes the numbers now (the app calls this when it closes).</summary>
    void Flush();

    /// <summary>Forgets everything and deletes the file.</summary>
    void Clear();
}

/// <inheritdoc cref="IPressCounter"/>
public sealed class PressCounter : IPressCounter
{
    /// <summary>The file is written at most this often (and on <see cref="Flush"/>).</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(2);

    /// <summary>The id of a key: where it is (<c>p:0</c> = page 1, <c>f:&lt;folder&gt;</c>) and its index, the same shape timers use.</summary>
    public static string KeyId(string place, int index) => $"{place}:{index}";

    private readonly string _path;
    private readonly Func<bool> _enabled;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();
    private PressData? _data;
    private DateTime _lastSave = DateTime.MinValue;
    private bool _dirty;

    public PressCounter(Func<bool> enabled, string? path = null, Func<DateTime>? now = null)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "presses.json");
        _now = now ?? (() => DateTime.Now);
    }

    public bool IsActive => _enabled();

    public DateTime? Since
    {
        get { lock (_lock) { EnsureLoaded(); return _data!.Profiles.Count == 0 ? null : _data.Since; } }
    }

    public void Count(string profileId, string controlId)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(controlId) || !_enabled()) return;

        lock (_lock)
        {
            EnsureLoaded();
            if (!_data!.Profiles.TryGetValue(profileId, out var controls))
            {
                // the first press of a fresh file is the moment counting began
                if (_data.Profiles.Count == 0) _data.Since = _now();
                controls = new Dictionary<string, long>();
                _data.Profiles[profileId] = controls;
            }

            controls[controlId] = controls.GetValueOrDefault(controlId) + 1;
            _dirty = true;
            SaveLocked(force: false);
        }
    }

    public IReadOnlyDictionary<string, long> Get(string profileId)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _data!.Profiles.TryGetValue(profileId, out var controls)
                ? new Dictionary<string, long>(controls)
                : new Dictionary<string, long>();
        }
    }

    public void Flush()
    {
        lock (_lock) SaveLocked(force: true);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _data = new PressData { Since = _now() };
            _dirty = false;
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch { /* the numbers are gone from memory; a locked file is removed at the next clear */ }
        }
    }

    private void EnsureLoaded()
    {
        if (_data != null) return;

        try
        {
            _data = File.Exists(_path) ? JsonSerializer.Deserialize<PressData>(File.ReadAllText(_path)) : null;
        }
        catch
        {
            _data = null;
        }

        _data ??= new PressData { Since = _now() };
    }

    private void SaveLocked(bool force)
    {
        if (!_dirty || _data == null) return;
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
            // tried again at the next press
        }
    }
}

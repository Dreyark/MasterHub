using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Audio;

/// <summary>How a mute key behaves (T-17 f).</summary>
public enum MutePressMode
{
    /// <summary>Each press switches mute on / off (as before).</summary>
    Toggle = 0,
    /// <summary>Muted; on (speaking) only while the key is held.</summary>
    PushToTalk = 1,
    /// <summary>On; muted only while the key is held (a cough button).</summary>
    PushToMute = 2
}

/// <summary>
/// The mute press modes (T-17 f): what the microphone should be after a key goes down or up. While held, the key does
/// nothing else (its Hold / Double bindings are not used).
/// </summary>
public static class MutePress
{
    /// <summary>The mute state wanted after the key event, or <c>null</c> for "toggle on press, nothing on release".</summary>
    public static bool? Wanted(MutePressMode mode, bool pressed) => mode switch
    {
        MutePressMode.PushToTalk => !pressed,   // held = speaking (not muted)
        MutePressMode.PushToMute => pressed,    // held = muted
        _ => null
    };

    /// <summary>Whether a toggle is needed to reach <paramref name="wanted"/>; with an unknown current state the caller toggles on every edge.</summary>
    public static bool NeedsToggle(bool? currentlyMuted, bool wanted) => currentlyMuted == null || currentlyMuted.Value != wanted;
}

/// <summary>Volume of the program in front and "everything else" (T-17 d, h).</summary>
public static class AppVolume
{
    public const int DefaultStepPercent = 5;

    /// <summary>A step in % (1–50) from a key parameter; empty / invalid gives the default.</summary>
    public static int ParseStep(string? parameter) =>
        int.TryParse((parameter ?? "").Trim().TrimEnd('%'), out int s) ? Math.Clamp(s, 1, 50) : DefaultStepPercent;

    public static float Step(float current, int direction, int stepPercent) =>
        Math.Clamp(current + Math.Sign(direction) * stepPercent / 100f, 0f, 1f);

    /// <summary>
    /// "Everything else" (as in deej): the sessions of the programs that no other control of the profile claims. MasterHub
    /// itself and nameless system sessions are left alone.
    /// </summary>
    public static IReadOnlyList<string> EverythingElse(IEnumerable<AudioSessionItem> sessions, IEnumerable<string> claimedProcesses)
    {
        var claimed = new HashSet<string>(claimedProcesses.Select(Normalize).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
        return sessions
            .Select(s => Normalize(s.ProcessName))
            .Where(n => n.Length > 0 && !claimed.Contains(n) && !n.StartsWith("masterhub", StringComparison.OrdinalIgnoreCase) && n != "idle")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Normalize(string? processName)
    {
        string n = (processName ?? "").Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }
}

/// <summary>
/// The Windows volume popup for faders (T-17 g). Windows has no API that shows its volume OSD, so the
/// volume is set one step (0.02) short, the real volume-up key is pressed (the popup appears and the volume moves up one
/// step), and after ~50 ms the exact level is set. Requests that arrive faster are merged: the newest wins.
/// </summary>
public sealed class VolumeOsd
{
    public const float StepShort = 0.02f;
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(50);

    private readonly Action<float> _setVolume;
    private readonly Action _pressVolumeUp;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly object _lock = new();
    private float? _pending;
    private bool _running;

    public VolumeOsd(Action<float> setVolume, Action pressVolumeUp, Func<TimeSpan, Task>? delay = null)
    {
        _setVolume = setVolume;
        _pressVolumeUp = pressVolumeUp;
        _delay = delay ?? (t => Task.Delay(t));
    }

    /// <summary>Asks for a level (0–1); returns the task of the run that will apply it (tests await it).</summary>
    public Task Request(float level)
    {
        lock (_lock)
        {
            _pending = Math.Clamp(level, 0f, 1f);
            if (_running) return Task.CompletedTask;   // the running loop picks the newest value up
            _running = true;
        }
        return Run();
    }

    private async Task Run()
    {
        while (true)
        {
            float target;
            lock (_lock)
            {
                if (_pending == null)
                {
                    _running = false;
                    return;
                }
                target = _pending.Value;
                _pending = null;
            }
            try
            {
                _setVolume(Math.Max(0f, target - StepShort));
                _pressVolumeUp();
                await _delay(Settle).ConfigureAwait(false);
                _setVolume(target);
            }
            catch
            {
                // a failed popup must not stop the fader
            }
        }
    }
}

/// <summary>
/// Knob / dial "modes" (T-17 i): 2–4 related turn jobs on one control, e.g. volume → scroll → track. A press moves to the
/// next mode; the mode's name is shown.
/// </summary>
public static class RollerModes
{
    public const int MinModes = 2, MaxModes = 4;

    /// <summary>The modes from text (<c>MasterVolume, ScrollVertical, MediaTrack</c>); unknown names, <c>None</c> and <c>Modes</c> are dropped.</summary>
    public static IReadOnlyList<Actions.RollerActionType> Parse(string? text)
    {
        var list = new List<Actions.RollerActionType>();
        foreach (string raw in (text ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse(raw.Trim(), true, out Actions.RollerActionType a) && Enum.IsDefined(a) && !int.TryParse(raw.Trim(), out _) &&
                a is not (Actions.RollerActionType.None or Actions.RollerActionType.Modes or Actions.RollerActionType.WindowsSensitivity) &&
                !list.Contains(a))
                list.Add(a);
            if (list.Count == MaxModes) break;
        }
        return list.Count >= MinModes ? list : Array.Empty<Actions.RollerActionType>();
    }

    public static int Next(int index, int count) => count <= 0 ? 0 : (Math.Max(0, index) + 1) % count;

    public static Actions.RollerActionType Current(IReadOnlyList<Actions.RollerActionType> modes, int index) =>
        modes.Count == 0 ? Actions.RollerActionType.None : modes[Math.Clamp(index, 0, modes.Count - 1)];
}

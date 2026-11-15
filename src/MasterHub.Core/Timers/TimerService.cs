using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MasterHub.Core.Timers;

/// <summary>
/// Which control a timer belongs to: the profile, the page (or folder) and the key. The same key on two profiles (or two
/// pages) is two independent timers.
/// </summary>
public readonly record struct TimerKey(string Profile, string Place, int Index)
{
    public override string ToString() => $"{Profile}/{Place}/{Index}";
}

/// <summary>A timer that is running, paused part-way or waiting to be acknowledged (the Studio lists these with a Stop button).</summary>
public sealed record ActiveTimer(TimerKey Key, string Caption, TimerSnapshot Snapshot);

public interface ITimerService
{
    /// <summary>A timer that is not running any more but is blinking, or one that runs, needs the one-second tick.</summary>
    bool NeedsTick { get; }

    /// <summary>A countdown or a Pomodoro phase ran out.</summary>
    event Action<TimerKey, TimerExpiry>? Expired;

    /// <summary>What a key shows now. A key whose timer was never started shows its configured time (nothing is created).</summary>
    TimerSnapshot Snapshot(TimerKey key, TimerKind kind, TimerSettings settings);

    /// <summary>Runs a command on the key's timer (created on first use). Returns the new state.</summary>
    TimerSnapshot Apply(TimerKey key, TimerKind kind, TimerSettings settings, string caption, TimerCommand command);

    /// <summary>A knob / dial step on a countdown.</summary>
    bool Adjust(TimerKey key, TimerKind kind, TimerSettings settings, int detents);

    /// <summary>The key was edited: an unstarted timer follows the new settings.</summary>
    void Reconfigure(TimerKey key, TimerKind kind, TimerSettings settings);

    /// <summary>Stops a timer and forgets it.</summary>
    void Stop(TimerKey key);

    IReadOnlyList<ActiveTimer> Active { get; }

    /// <summary>One step of the clock: notices expiries and returns the keys whose picture has to change.</summary>
    IReadOnlyList<TimerKey> Tick();
}

/// <summary>
/// Timers keyed by <see cref="TimerKey"/>, on a monotonic clock. Timers are volatile: they are not saved with the profile
/// and are gone when the app closes. Nothing here draws or touches the hardware.
/// </summary>
public sealed class TimerService : ITimerService
{
    private sealed class Entry
    {
        public required DeckTimer Timer;
        public string Caption = "";
        public string LastSignature = "";
    }

    private readonly object _gate = new();
    private readonly Dictionary<TimerKey, Entry> _timers = new();
    private readonly Func<TimeSpan> _clock;

    public TimerService() : this(null) { }

    /// <param name="clock">Monotonic time; tests pass a fake one.</param>
    public TimerService(Func<TimeSpan>? clock)
    {
        if (clock == null)
        {
            long start = Stopwatch.GetTimestamp();
            clock = () => Stopwatch.GetElapsedTime(start);
        }
        _clock = clock;
    }

    public event Action<TimerKey, TimerExpiry>? Expired;

    public bool NeedsTick
    {
        get
        {
            lock (_gate)
            {
                var now = _clock();
                return _timers.Values.Any(e => e.Timer.WantsTick(now));
            }
        }
    }

    public TimerSnapshot Snapshot(TimerKey key, TimerKind kind, TimerSettings settings)
    {
        lock (_gate)
        {
            var now = _clock();
            if (_timers.TryGetValue(key, out var entry) && entry.Timer.Kind == kind) return entry.Timer.Snapshot(now);
            return new DeckTimer(kind, settings).Snapshot(now);
        }
    }

    public TimerSnapshot Apply(TimerKey key, TimerKind kind, TimerSettings settings, string caption, TimerCommand command)
    {
        TimerExpiry? expiry = null;
        TimerSnapshot snapshot;

        lock (_gate)
        {
            var now = _clock();
            var entry = GetOrCreate(key, kind, settings, caption);

            // the clock may have passed the end before this press: report that first (once)
            expiry = entry.Timer.Poll(now);
            entry.Timer.Apply(command, now);

            snapshot = entry.Timer.Snapshot(now);
            entry.LastSignature = snapshot.Signature;
            entry.Caption = string.IsNullOrWhiteSpace(caption) ? entry.Caption : caption;

            if (snapshot.State == TimerState.Idle) _timers.Remove(key);   // back to the start: nothing to keep
        }

        if (expiry is { } e) Expired?.Invoke(key, e);
        return snapshot;
    }

    public bool Adjust(TimerKey key, TimerKind kind, TimerSettings settings, int detents)
    {
        lock (_gate)
        {
            var entry = GetOrCreate(key, kind, settings, "");
            bool changed = entry.Timer.Adjust(detents, _clock());
            if (entry.Timer.IsIdle && !changed) _timers.Remove(key);
            return changed;
        }
    }

    public void Reconfigure(TimerKey key, TimerKind kind, TimerSettings settings)
    {
        lock (_gate)
        {
            if (!_timers.TryGetValue(key, out var entry)) return;
            if (entry.Timer.Kind != kind) { _timers.Remove(key); return; }   // the key became another kind of timer

            entry.Timer.Reconfigure(settings);
            if (entry.Timer.IsIdle) _timers.Remove(key);
        }
    }

    public void Stop(TimerKey key)
    {
        lock (_gate) _timers.Remove(key);
    }

    public IReadOnlyList<ActiveTimer> Active
    {
        get
        {
            lock (_gate)
            {
                var now = _clock();
                return _timers
                    .Select(p => new ActiveTimer(p.Key, p.Value.Caption, p.Value.Timer.Snapshot(now)))
                    .Where(t => t.Snapshot.State != TimerState.Idle)
                    .OrderBy(t => t.Key.ToString(), StringComparer.Ordinal)
                    .ToList();
            }
        }
    }

    public IReadOnlyList<TimerKey> Tick()
    {
        var changed = new List<TimerKey>();
        var expiries = new List<(TimerKey, TimerExpiry)>();

        lock (_gate)
        {
            var now = _clock();
            foreach (var (key, entry) in _timers)
            {
                if (entry.Timer.Poll(now) is { } expiry) expiries.Add((key, expiry));

                string signature = entry.Timer.Snapshot(now).Signature;
                if (signature == entry.LastSignature) continue;
                entry.LastSignature = signature;
                changed.Add(key);
            }
        }

        foreach (var (key, expiry) in expiries) Expired?.Invoke(key, expiry);
        return changed;
    }

    // a timer of another kind than the key now has is replaced (the key was switched from a countdown to a stopwatch, say)
    private Entry GetOrCreate(TimerKey key, TimerKind kind, TimerSettings settings, string caption)
    {
        if (_timers.TryGetValue(key, out var entry) && entry.Timer.Kind == kind)
        {
            entry.Timer.Reconfigure(settings);
            return entry;
        }

        entry = new Entry { Timer = new DeckTimer(kind, settings), Caption = caption };
        _timers[key] = entry;
        return entry;
    }
}

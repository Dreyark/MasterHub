using System;
using System.Collections.Generic;

namespace MasterHub.Core.Actions;

/// <summary>The three things one key can do (T-05): a tap, a hold, a double tap.</summary>
public enum KeyGesture
{
    Press,
    Hold,
    Double
}

/// <summary>Which extra gestures a key has bindings for. A key with neither fires its tap on the way down, as always.</summary>
/// <param name="Hold">The key has a Hold binding.</param>
/// <param name="Double">The key has a Double binding.</param>
/// <param name="HoldMs">Hold time for this key; <c>null</c> = the recogniser's default (a destructive Hold binding asks for longer).</param>
public readonly record struct GestureBindings(bool Hold, bool Double, int? HoldMs = null)
{
    public bool Any => Hold || Double;
}

/// <summary>
/// Pure press / hold / double-tap state machine: no timers, no threads, no clock
/// of its own – every call takes the time, so tests never sleep. The owner calls <see cref="Tick"/> at
/// <see cref="NextDeadline"/> to let a hold or a double-tap window expire.
/// <para>
/// With no extra bindings the tap fires <b>on the way down</b>, exactly as before. With a Hold and/or Double binding the
/// tap has to wait to be told apart: it fires on <b>release</b> (Hold only) or when the double-tap window closes (Double).
/// </para>
/// </summary>
public sealed class GestureRecognizer
{
    private enum Phase
    {
        Idle,
        PlainDown,       // tap already fired on the way down; waiting for the release
        Down,            // down, tap not decided yet
        Held,            // hold fired; waiting for the release (no tap)
        WaitSecond,      // released once, Double is bound: waiting for a second tap
        DoubleDown       // the double fired; waiting for the release
    }

    private struct KeyState
    {
        public Phase Phase;
        public long DownAt;
        public long ReleasedAt;
        public GestureBindings Bound;
    }

    private readonly Dictionary<int, KeyState> _keys = new();

    public GestureRecognizer(int holdMs = 500, int doubleMs = 300)
    {
        HoldMs = holdMs;
        DoubleMs = doubleMs;
    }

    /// <summary>Default time a key must be held for the Hold gesture.</summary>
    public int HoldMs { get; set; }

    /// <summary>Longest gap between the first release and the second press of a double tap.</summary>
    public int DoubleMs { get; set; }

    /// <summary>A key went down (<paramref name="down"/> = true) or up. Returns the gestures that fire right now.</summary>
    public IReadOnlyList<KeyGesture> OnKey(int key, bool down, GestureBindings bound, long nowMs)
    {
        var fired = new List<KeyGesture>(2);
        _keys.TryGetValue(key, out var s);

        // A window or hold that already ran out but was not ticked yet must not be missed.
        Expire(ref s, key, nowMs, fired);

        if (down)
        {
            switch (s.Phase)
            {
                case Phase.WaitSecond: // second tap inside the window
                    fired.Add(KeyGesture.Double);
                    s.Phase = Phase.DoubleDown;
                    break;
                case Phase.Idle:
                    s.Bound = bound;
                    s.DownAt = nowMs;
                    if (!bound.Any)
                    {
                        fired.Add(KeyGesture.Press);
                        s.Phase = Phase.PlainDown;
                    }
                    else
                    {
                        s.Phase = Phase.Down;
                    }
                    break;
                // already down (auto-repeat / duplicate event): ignore
            }
        }
        else
        {
            switch (s.Phase)
            {
                case Phase.Down:
                    if (s.Bound.Double)
                    {
                        s.Phase = Phase.WaitSecond;
                        s.ReleasedAt = nowMs;
                    }
                    else
                    {
                        fired.Add(KeyGesture.Press); // Hold only: the tap fires on release
                        s.Phase = Phase.Idle;
                    }
                    break;
                case Phase.PlainDown:
                case Phase.Held:
                case Phase.DoubleDown:
                    s.Phase = Phase.Idle;
                    break;
                // WaitSecond / Idle + up: nothing
            }
        }

        _keys[key] = s;
        return fired;
    }

    /// <summary>Lets holds and double-tap windows that expired by <paramref name="nowMs"/> fire. Returns (key, gesture) pairs.</summary>
    public IReadOnlyList<(int Key, KeyGesture Gesture)> Tick(long nowMs)
    {
        var result = new List<(int, KeyGesture)>();
        foreach (int key in new List<int>(_keys.Keys))
        {
            var s = _keys[key];
            var fired = new List<KeyGesture>(1);
            Expire(ref s, key, nowMs, fired);
            _keys[key] = s;
            foreach (var g in fired) result.Add((key, g));
        }
        return result;
    }

    /// <summary>The time at which <see cref="Tick"/> will have something to do, or <c>null</c> when nothing is pending.</summary>
    public long? NextDeadline
    {
        get
        {
            long? next = null;
            foreach (var s in _keys.Values)
            {
                long? due = s.Phase switch
                {
                    Phase.Down when s.Bound.Hold => s.DownAt + HoldFor(s.Bound),
                    Phase.WaitSecond => s.ReleasedAt + DoubleMs,
                    _ => null
                };
                if (due is { } d && (next == null || d < next)) next = d;
            }
            return next;
        }
    }

    /// <summary>Forgets every key (e.g. when the profile changes).</summary>
    public void Reset() => _keys.Clear();

    private int HoldFor(GestureBindings bound) => bound.HoldMs ?? HoldMs;

    private void Expire(ref KeyState s, int key, long nowMs, List<KeyGesture> fired)
    {
        if (s.Phase == Phase.Down && s.Bound.Hold && nowMs - s.DownAt >= HoldFor(s.Bound))
        {
            fired.Add(KeyGesture.Hold);
            s.Phase = Phase.Held;
        }
        else if (s.Phase == Phase.WaitSecond && nowMs - s.ReleasedAt > DoubleMs)
        {
            fired.Add(KeyGesture.Press); // the window closed without a second tap: it was a single tap
            s.Phase = Phase.Idle;
        }
    }
}

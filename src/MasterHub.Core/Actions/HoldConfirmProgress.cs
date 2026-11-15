using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Actions;

/// <summary>
/// How far a hold-to-confirm key has been held (T-03): a power key (shutdown / restart / sleep) needs a
/// <see cref="PowerActionGuard.HoldToConfirmMs"/> hold, and the key shows a bar that fills while it is held, so the user
/// sees that holding works and how long is left. Release before the end cancels (the bar disappears). No timers inside:
/// the time comes from the clock given, so tests need no sleeping.
/// </summary>
public sealed class HoldConfirmProgress
{
    /// <summary>The bar is redrawn this often while a key is held (10 pictures/s is smooth enough on a 72 px key).</summary>
    public const int FrameMs = 100;

    /// <summary>The bar moves in this many steps, so equal steps give equal pictures (and the device dedupe can skip repeats).</summary>
    public const int Steps = 20;

    private readonly Func<long> _clockMs;
    private readonly Dictionary<int, (long startMs, int durationMs)> _held = new();

    public HoldConfirmProgress(Func<long>? clockMs = null)
    {
        _clockMs = clockMs ?? (() => Environment.TickCount64);
    }

    public bool IsAnyRunning => _held.Count > 0;

    public IReadOnlyList<int> Running => _held.Keys.OrderBy(k => k).ToList();

    public bool IsRunning(int key) => _held.ContainsKey(key);

    /// <summary>Starts (or restarts) the bar of a key; a duration of 0 or less does nothing.</summary>
    public void Start(int key, int durationMs)
    {
        if (durationMs <= 0) return;
        _held[key] = (_clockMs(), durationMs);
    }

    /// <summary>Stops the bar; <c>true</c> when it was running (the key must be drawn again without it).</summary>
    public bool Stop(int key) => _held.Remove(key);

    public void Clear() => _held.Clear();

    /// <summary>0 … 1 of the hold done, rounded down to one of <see cref="Steps"/> steps; 0 for a key that is not held.</summary>
    public double Fraction(int key)
    {
        if (!_held.TryGetValue(key, out var h)) return 0;
        double raw = Math.Clamp((_clockMs() - h.startMs) / (double)h.durationMs, 0, 1);
        return Math.Floor(raw * Steps) / Steps;
    }

    /// <summary>How long a key must be held for its power action, or <c>null</c> when it needs no hold-to-confirm.</summary>
    public static int? DurationFor(DeckActionType tapAction, bool skipConfirmation, bool hasGestures, int? gestureHoldMs)
    {
        if (hasGestures) return gestureHoldMs;   // with gestures only a destructive Hold binding waits (the tap fires on release)
        return PowerActionGuard.Resolve(tapAction, KeyPressMode.OnPress, skipConfirmation).HoldMs;
    }
}

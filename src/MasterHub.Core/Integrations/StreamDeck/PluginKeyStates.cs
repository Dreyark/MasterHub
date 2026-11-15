using System;
using System.Collections.Concurrent;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>
/// The state of every plugin key (T-31 d): Stream Deck actions may define <b>more than two states</b> in their manifest
/// (a 4-way mode switch, say). The plugin chooses one with <c>setState</c>, and – unless the action sets
/// <c>DisableAutomaticStates</c> – the app moves to the next state itself after each key-up, wrapping around. The state is
/// sent in <c>keyDown</c> / <c>keyUp</c> / <c>willAppear</c> so the plugin always knows it.
/// </summary>
public sealed class PluginKeyStates
{
    private readonly ConcurrentDictionary<int, int> _states = new();

    /// <summary>A state index kept inside <c>0 … count-1</c> (0 when the action has no states).</summary>
    public static int Clamp(int state, int stateCount) => stateCount <= 0 ? 0 : Math.Clamp(state, 0, stateCount - 1);

    /// <summary>The state after a key-up: the next one (wrapping) for a multi-state action, the same one otherwise.</summary>
    public static int Next(int state, int stateCount, bool disableAutomaticStates) =>
        stateCount < 2 || disableAutomaticStates ? Clamp(state, stateCount) : (Clamp(state, stateCount) + 1) % stateCount;

    public int Get(int key) => _states.TryGetValue(key, out int s) ? s : 0;

    /// <summary>Sets the state the plugin asked for; returns the state actually kept.</summary>
    public int Set(int key, int state, int stateCount)
    {
        int s = Clamp(state, stateCount);
        _states[key] = s;
        return s;
    }

    /// <summary>
    /// After a key-up: returns the state to report in the event (the state the key had while pressed) and the new state.
    /// <paramref name="changed"/> tells the caller to redraw the key.
    /// </summary>
    public (int Reported, int Current) AfterKeyUp(int key, int stateCount, bool disableAutomaticStates, out bool changed)
    {
        int before = Clamp(Get(key), stateCount);
        int after = Next(before, stateCount, disableAutomaticStates);
        _states[key] = after;
        changed = after != before;
        return (before, after);
    }

    public void Forget(int key) => _states.TryRemove(key, out _);

    public void Clear() => _states.Clear();
}

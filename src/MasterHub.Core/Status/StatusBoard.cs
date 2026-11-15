using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Status;

/// <summary>The six states every connected app shares (T-25): they decide the icon, the colour and whether the deck asks for attention.</summary>
public enum AppState
{
    /// <summary>Grey dot: nothing going on.</summary>
    Idle = 0,
    /// <summary>Orange: it waits for the user. The deck's edge pulses orange.</summary>
    Waiting = 1,
    /// <summary>Blue: busy.</summary>
    Working = 2,
    /// <summary>Green check.</summary>
    Done = 3,
    /// <summary>Red cross.</summary>
    Error = 4,
    /// <summary>Dim dash: the app does not answer.</summary>
    Offline = 5
}

/// <summary>One thing an app reports: a build, a session, a download.</summary>
/// <param name="Source">The app (e.g. <c>claude</c>).</param>
/// <param name="Id">The item inside the app; unique per source.</param>
/// <param name="Label">Short name shown to the user (a project folder name, a job name).</param>
/// <param name="Text">Optional one-line detail. Never a prompt or a message.</param>
/// <param name="Value">Optional value (a percentage, a count) as text.</param>
public sealed record StatusItem(string Source, string Id, AppState State, string? Label, string? Text, string? Value, DateTime UpdatedAt);

/// <summary>Everything the connected apps currently report. In memory only: it is rebuilt by the apps after a restart.</summary>
public interface IStatusBoard
{
    /// <summary>All items, most urgent first (see <see cref="StatusBoard.Rank"/>).</summary>
    IReadOnlyList<StatusItem> Items { get; }

    /// <summary>Raised (on the thread that changed the board) when any item was added, changed or removed.</summary>
    event Action? Changed;

    /// <summary>Replaces everything one source reports. An empty list removes the source.</summary>
    /// <param name="timeToLive">Items of this source nobody refreshed for this long disappear; <c>null</c> = they stay.</param>
    void Replace(string source, IReadOnlyList<StatusItem> items, TimeSpan? timeToLive = null);

    /// <summary>Adds or replaces one item of a source (the other items of that source stay).</summary>
    void Set(StatusItem item, TimeSpan? timeToLive = null);

    /// <summary>Removes one item; <c>true</c> when it was there.</summary>
    bool Remove(string source, string id);

    /// <summary>Removes everything of a source.</summary>
    void RemoveSource(string source);

    int Count(AppState state);
}

/// <inheritdoc cref="IStatusBoard"/>
public sealed class StatusBoard : IStatusBoard
{
    private readonly object _lock = new();
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<(string Source, string Id), StatusItem> _items = new();
    private readonly Dictionary<string, TimeSpan> _ttl = new(StringComparer.OrdinalIgnoreCase);

    public StatusBoard(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    public event Action? Changed;

    /// <summary>Most urgent first: waiting for the user, then errors, then finished, working, idle, offline.</summary>
    public static int Rank(AppState state) => state switch
    {
        AppState.Waiting => 0,
        AppState.Error => 1,
        AppState.Done => 2,
        AppState.Working => 3,
        AppState.Idle => 4,
        _ => 5
    };

    public IReadOnlyList<StatusItem> Items
    {
        get
        {
            bool expired;
            List<StatusItem> list;
            lock (_lock)
            {
                expired = PruneLocked();
                list = _items.Values.OrderBy(i => Rank(i.State)).ThenBy(i => i.Source, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.Label ?? i.Id, StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (expired) RaiseChanged();
            return list;
        }
    }

    public void Replace(string source, IReadOnlyList<StatusItem> items, TimeSpan? timeToLive = null)
    {
        lock (_lock)
        {
            foreach (var key in _items.Keys.Where(k => string.Equals(k.Source, source, StringComparison.OrdinalIgnoreCase)).ToList())
                _items.Remove(key);

            foreach (var item in items) _items[(item.Source.ToLowerInvariant(), item.Id)] = item;
            Remember(source, timeToLive, items.Count > 0);
            PruneLocked();
        }

        RaiseChanged();
    }

    public void Set(StatusItem item, TimeSpan? timeToLive = null)
    {
        lock (_lock)
        {
            _items[(item.Source.ToLowerInvariant(), item.Id)] = item;
            Remember(item.Source, timeToLive, true);
            PruneLocked();
        }

        RaiseChanged();
    }

    public bool Remove(string source, string id)
    {
        bool removed;
        lock (_lock) removed = _items.Remove((source.ToLowerInvariant(), id));
        if (removed) RaiseChanged();
        return removed;
    }

    public void RemoveSource(string source) => Replace(source, Array.Empty<StatusItem>());

    public int Count(AppState state)
    {
        lock (_lock)
        {
            PruneLocked();
            return _items.Values.Count(i => i.State == state);
        }
    }

    private void Remember(string source, TimeSpan? ttl, bool any)
    {
        if (ttl.HasValue && any) _ttl[source] = ttl.Value;
        else if (!any) _ttl.Remove(source);
    }

    /// <summary>Drops the items of sources that were not refreshed within their time to live. Call with the lock held.</summary>
    private bool PruneLocked()
    {
        if (_ttl.Count == 0) return false;

        var now = _clock();
        var dead = _items.Where(p => _ttl.TryGetValue(p.Key.Source, out var ttl) && now - p.Value.UpdatedAt > ttl).Select(p => p.Key).ToList();
        foreach (var key in dead) _items.Remove(key);
        return dead.Count > 0;
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); } catch { /* a listener must not break the board */ }
    }

    /// <summary>Parses <c>waiting working done error idle offline</c> (any case). <c>false</c> for anything else.</summary>
    public static bool TryParseState(string? text, out AppState state)
    {
        state = AppState.Idle;
        return !string.IsNullOrWhiteSpace(text) && !char.IsDigit(text.Trim()[0]) &&
               Enum.TryParse(text.Trim(), ignoreCase: true, out state) && Enum.IsDefined(state);
    }
}

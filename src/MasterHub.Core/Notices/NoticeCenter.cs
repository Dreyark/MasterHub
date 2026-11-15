using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Notices;

public enum NoticeLevel { Info, Warning, Error }

/// <summary>One banner in the window: a short text, how serious it is, and the key that tells repeats apart.</summary>
public sealed record Notice(long Id, NoticeLevel Level, string Text, string Key, DateTime At);

/// <summary>
/// Problems shown as <b>banners in the app window</b> instead of OS message boxes or a log line only (T-34 #8): a key whose
/// action failed, a program in front that runs as administrator, ... Repeats of the same thing are merged, a few banners
/// at most are shown, and information / warnings go away by themselves.
/// </summary>
public interface INoticeCenter
{
    IReadOnlyList<Notice> Current { get; }

    /// <summary>Raised (on the caller's thread) whenever <see cref="Current"/> changes.</summary>
    event Action? Changed;

    /// <summary>Shows a banner; the same <paramref name="key"/> again within the merge time only refreshes the old one.</summary>
    Notice Post(NoticeLevel level, string text, string? key = null);

    void Dismiss(long id);

    void DismissAll();

    /// <summary>Removes banners whose time is up; called by the window about once a second.</summary>
    void Prune();
}

/// <inheritdoc cref="INoticeCenter"/>
public sealed class NoticeCenter : INoticeCenter
{
    /// <summary>At most this many banners at once; a new one pushes the oldest out.</summary>
    public const int MaxShown = 4;

    /// <summary>The same key within this time does not make a second banner.</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(30);

    /// <summary>How long information and warnings stay. Errors stay until dismissed (or pushed out).</summary>
    public static readonly TimeSpan InfoLifetime = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan WarningLifetime = TimeSpan.FromSeconds(12);

    private static readonly Lazy<NoticeCenter> _shared = new(() => new NoticeCenter());

    /// <summary>The app's one notice center (also registered in DI), so code without DI access can post.</summary>
    public static NoticeCenter Shared => _shared.Value;

    private readonly object _lock = new();
    private readonly List<Notice> _notices = new();
    private readonly Func<DateTime> _clock;
    private long _nextId = 1;

    public NoticeCenter(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public event Action? Changed;

    public IReadOnlyList<Notice> Current
    {
        get { lock (_lock) return _notices.ToList(); }
    }

    public Notice Post(NoticeLevel level, string text, string? key = null)
    {
        string k = string.IsNullOrWhiteSpace(key) ? level + ":" + text : key;
        string shown = (text ?? "").Trim();
        if (shown.Length > 300) shown = shown[..300] + "…";
        Notice notice;
        lock (_lock)
        {
            DateTime now = _clock();
            int existing = _notices.FindIndex(n => n.Key == k);
            if (existing >= 0 && now - _notices[existing].At < MergeWindow)
            {
                // the same thing again: keep one banner with the newest text and time
                notice = _notices[existing] with { Text = shown, Level = Max(level, _notices[existing].Level), At = now };
                _notices[existing] = notice;
            }
            else
            {
                if (existing >= 0) _notices.RemoveAt(existing);
                notice = new Notice(_nextId++, level, shown, k, now);
                _notices.Add(notice);
                while (_notices.Count > MaxShown) _notices.RemoveAt(0);
            }
        }
        Changed?.Invoke();
        return notice;
    }

    public void Dismiss(long id)
    {
        bool removed;
        lock (_lock) removed = _notices.RemoveAll(n => n.Id == id) > 0;
        if (removed) Changed?.Invoke();
    }

    public void DismissAll()
    {
        bool any;
        lock (_lock)
        {
            any = _notices.Count > 0;
            _notices.Clear();
        }
        if (any) Changed?.Invoke();
    }

    public void Prune()
    {
        bool removed;
        lock (_lock)
        {
            DateTime now = _clock();
            removed = _notices.RemoveAll(n => Lifetime(n.Level) is { } life && now - n.At >= life) > 0;
        }
        if (removed) Changed?.Invoke();
    }

    private static TimeSpan? Lifetime(NoticeLevel level) => level switch
    {
        NoticeLevel.Info => InfoLifetime,
        NoticeLevel.Warning => WarningLifetime,
        _ => null
    };

    private static NoticeLevel Max(NoticeLevel a, NoticeLevel b) => (NoticeLevel)Math.Max((int)a, (int)b);
}

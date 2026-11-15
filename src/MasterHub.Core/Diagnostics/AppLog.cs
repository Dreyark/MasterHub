using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Diagnostics;

/// <summary>
/// The app's own log lines (autostart, outbound calls, …) for the Logs page. Lines written before the page exists are
/// kept in a small ring buffer and shown when it opens; nothing is written to disk here.
/// </summary>
public static class AppLog
{
    public const int Capacity = 200;

    private static readonly object _lock = new();
    private static readonly Queue<(DateTime At, string Text)> _recent = new();

    /// <summary>Raised for every new line (on the writer's thread).</summary>
    public static event Action<DateTime, string>? Written;

    public static void Write(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var at = DateTime.Now;
        lock (_lock)
        {
            _recent.Enqueue((at, text));
            while (_recent.Count > Capacity) _recent.Dequeue();
        }
        System.Diagnostics.Debug.WriteLine(text);
        try
        {
            Written?.Invoke(at, text);
        }
        catch
        {
            // a broken listener must not break the writer
        }
    }

    /// <summary>The lines kept so far, oldest first.</summary>
    public static IReadOnlyList<(DateTime At, string Text)> Recent()
    {
        lock (_lock) return _recent.ToList();
    }

    /// <summary>For tests.</summary>
    internal static void Clear()
    {
        lock (_lock) _recent.Clear();
    }
}

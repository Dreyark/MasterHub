using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

namespace MasterHub.Core.Diagnostics;

/// <summary>
/// Counts outbound HTTP calls per host and writes one log line a minute (T-34 #5), so it is easy to see which service the
/// app talks to and how often (this once exposed a poll that made 2 400 calls an hour). It listens to .NET's own
/// <c>System.Net.Http</c> metrics (<c>http.client.request.duration</c>, tag <c>server.address</c>), so every
/// <see cref="System.Net.Http.HttpClient"/> in the app is counted without changing the services. Only host names are
/// kept – no paths, queries or tokens – and nothing leaves the PC.
/// </summary>
public sealed class OutboundCallCounter : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    private const string MeterName = "System.Net.Http";
    private const string InstrumentName = "http.client.request.duration";
    private const string HostTag = "server.address";

    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private MeterListener? _listener;
    private Timer? _timer;

    /// <summary>Counts one call to <paramref name="host"/> (also used by tests).</summary>
    public void Record(string? host)
    {
        string h = string.IsNullOrWhiteSpace(host) ? "?" : host.Trim().ToLowerInvariant();
        _counts.AddOrUpdate(h, 1, (_, n) => n + 1);
    }

    /// <summary>The counts since the last drain, the busiest host first, and starts counting again.</summary>
    public IReadOnlyList<(string Host, int Count)> Drain()
    {
        var result = new List<(string, int)>();
        foreach (var host in _counts.Keys.ToList())
        {
            if (_counts.TryRemove(host, out int n) && n > 0) result.Add((host, n));
        }
        return result.OrderByDescending(r => r.Item2).ThenBy(r => r.Item1, StringComparer.Ordinal).ToList();
    }

    /// <summary>The log line for one interval, or <c>null</c> when there were no calls.</summary>
    public static string? Format(IReadOnlyList<(string Host, int Count)> counts, TimeSpan interval)
    {
        if (counts.Count == 0) return null;
        int total = counts.Sum(c => c.Count);
        string list = string.Join(", ", counts.Take(12).Select(c => $"{c.Host} ×{c.Count}"));
        if (counts.Count > 12) list += $", +{counts.Count - 12} more";
        return $"[Network] {total} outbound call(s) in the last {(int)Math.Round(interval.TotalSeconds)} s: {list}";
    }

    /// <summary>Starts listening and writing a line every <paramref name="interval"/> through <paramref name="write"/>.</summary>
    public void Start(Action<string> write, TimeSpan? interval = null)
    {
        if (_listener != null) return;
        var every = interval ?? DefaultInterval;

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MeterName && instrument.Name == InstrumentName) listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == HostTag)
                {
                    Record(tag.Value as string);
                    return;
                }
            }
            Record(null);
        });
        _listener.Start();

        _timer = new Timer(_ =>
        {
            try
            {
                if (Format(Drain(), every) is { } line) write(line);
            }
            catch
            {
                // counting is a help; it must never disturb the app
            }
        }, null, every, every);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _listener?.Dispose();
        _listener = null;
    }
}

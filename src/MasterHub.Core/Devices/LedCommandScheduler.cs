using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MasterHub.Core.Devices;

/// <summary>
/// The lighting effects the M0 modules (base, faders, knobs) understand (<c>0x02 0x2C</c>). Same values as the knob mode the
/// profiles already store, so existing profiles keep working. There is no per-LED addressing and no roller LEDs.
/// </summary>
public enum LedEffect : byte
{
    Disable = 0,
    Static = 1,
    Breathing = 2,
    Strobe = 3,
    Spring = 4,
    Cycling = 5,
    Random = 6,
    Wave = 7,
    Water = 8,
    Rainbow = 9
}

/// <summary>Which module a LED command goes to.</summary>
public enum LedDevice : byte
{
    Base = 0,
    Faders = 1,
    Rollers = 2,
    Knobs = 3
}

/// <summary>What the command changes.</summary>
public enum LedKind : byte
{
    Mode = 0,
    Color = 1,
    Brightness = 2,
    Speed = 3
}

/// <summary>One LED report: <c>Index</c> 0xFF = all LEDs of the module (the base is only addressed as 0).</summary>
public readonly record struct LedCommand(LedDevice Device, byte Index, LedKind Kind, byte V1 = 0, byte V2 = 0, byte V3 = 0, bool Droppable = false)
{
    internal (LedDevice, byte, LedKind) Key => (Device, Index, Kind);
}

/// <summary>
/// Single gate for every <b>live</b> LED update (T-18). It was found that the faders <b>dropped off the USB bus at 50 LED
/// updates per second</b>, so: commands are coalesced per target (the latest wins), diffed against what was last sent, and
/// knobs/faders get at least 40 ms between sends (the base is exempt). Commands marked droppable (lights that follow music)
/// wait instead of queueing – a stale one is dropped, never shown late. Sending happens on a thread of its own so a rainbow
/// never holds the key pictures back. Time is a parameter of <see cref="Pump"/>, so tests need no sleeping.
/// </summary>
public sealed class LedCommandScheduler : IDisposable
{
    /// <summary>The measured safe gap between sends to one knobs/faders module.</summary>
    public const int ModuleGapMs = 40;

    /// <summary>A droppable command older than this when its turn comes is dropped.</summary>
    public const int StaleMs = 100;

    private sealed class Pending
    {
        public required LedCommand Command { get; init; }
        public long Sequence { get; init; }
        public long EnqueuedAt { get; init; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<(LedDevice, byte, LedKind), Pending> _pending = new();
    private readonly Dictionary<(LedDevice, byte, LedKind), (byte, byte, byte)> _lastSent = new();
    private readonly long[] _nextAllowed = new long[4];
    private readonly Func<LedCommand, int> _send;
    private readonly Func<long> _nowMs;
    private readonly Thread? _thread;
    private long _sequence;
    private bool _disposed;

    /// <param name="send">Writes one command to the hardware; returns 0 on success.</param>
    /// <param name="nowMs">Monotonic clock in ms (default: <see cref="Stopwatch"/>).</param>
    /// <param name="startThread">False in tests: drive the scheduler with <see cref="Pump"/>.</param>
    public LedCommandScheduler(Func<LedCommand, int> send, Func<long>? nowMs = null, bool startThread = true)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _nowMs = nowMs ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);

        if (startThread)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "MasterHub-LedScheduler" };
            _thread.Start();
        }
    }

    public long SentCount { get; private set; }
    public long CoalescedCount { get; private set; }
    public long SkippedUnchangedCount { get; private set; }
    public long DroppedStaleCount { get; private set; }

    /// <summary>Number of commands waiting to be sent.</summary>
    public int PendingCount
    {
        get { lock (_lock) { return _pending.Count; } }
    }

    /// <summary>Queues a command. Returns immediately; the worker sends it as soon as the module's gap allows.</summary>
    public void Submit(LedCommand command)
    {
        lock (_lock)
        {
            if (_disposed) return;

            var key = command.Key;
            if (_pending.ContainsKey(key)) CoalescedCount++;
            _pending[key] = new Pending { Command = command, Sequence = ++_sequence, EnqueuedAt = _nowMs() };
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    /// Sends every pending command whose module is allowed to receive one at <paramref name="now"/> and returns the time at
    /// which the next one may go (<c>null</c> when nothing is waiting).
    /// </summary>
    public long? Pump(long now)
    {
        List<Pending> due;
        lock (_lock)
        {
            due = _pending.Values.OrderBy(p => p.Sequence).ToList();
        }

        long? next = null;
        foreach (var pending in due)
        {
            var c = pending.Command;
            int dev = (int)c.Device;

            lock (_lock)
            {
                // superseded while we were looking (a newer command took its place)
                if (!_pending.TryGetValue(c.Key, out var current) || current.Sequence != pending.Sequence) continue;

                if (c.Droppable && now - pending.EnqueuedAt > StaleMs)
                {
                    _pending.Remove(c.Key);
                    DroppedStaleCount++;
                    continue;
                }

                if (_lastSent.TryGetValue(c.Key, out var last) && last == (c.V1, c.V2, c.V3))
                {
                    _pending.Remove(c.Key);
                    SkippedUnchangedCount++;
                    continue;
                }

                if (now < _nextAllowed[dev])
                {
                    long at = _nextAllowed[dev];
                    next = next == null ? at : Math.Min(next.Value, at);
                    continue;
                }

                _pending.Remove(c.Key);
            }

            int result;
            try { result = _send(c); }
            catch { result = -1; }

            lock (_lock)
            {
                SentCount++;
                _nextAllowed[dev] = now + (c.Device == LedDevice.Base ? 0 : ModuleGapMs);
                if (result == 0) Remember(c);
            }
        }

        lock (_lock)
        {
            return _pending.Count == 0 ? null : next ?? now;
        }
    }

    // After an "all LEDs" command the individual values are unknown, and after an individual one "all" is no longer true.
    private void Remember(LedCommand c)
    {
        var stale = _lastSent.Keys
            .Where(k => k.Item1 == c.Device && k.Item3 == c.Kind && (c.Index == 0xFF ? k.Item2 != 0xFF : k.Item2 == 0xFF))
            .ToList();
        foreach (var key in stale) _lastSent.Remove(key);

        _lastSent[c.Key] = (c.V1, c.V2, c.V3);
    }

    /// <summary>Forgets what was sent (after a connect, wake-up or a direct write) so the next commands are sent in full.</summary>
    public void Invalidate()
    {
        lock (_lock) { _lastSent.Clear(); }
    }

    /// <summary>Drops everything that is waiting (standby, disconnect): a late colour must never light a sleeping deck.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _pending.Clear();
            _lastSent.Clear();
        }
    }

    private void Run()
    {
        while (true)
        {
            long? next;
            lock (_lock)
            {
                while (!_disposed && _pending.Count == 0) Monitor.Wait(_lock);
                if (_disposed) return;
            }

            long now = _nowMs();
            next = Pump(now);

            if (next is long at && at > now)
            {
                lock (_lock)
                {
                    if (!_disposed) Monitor.Wait(_lock, (int)Math.Min(at - now, ModuleGapMs));
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _pending.Clear();
            Monitor.PulseAll(_lock);
        }
        _thread?.Join(500);
    }
}

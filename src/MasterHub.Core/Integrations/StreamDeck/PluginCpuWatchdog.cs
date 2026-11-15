using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>Reads how much processor time a process has used so far (a fake in tests).</summary>
public interface IProcessCpuSampler
{
    /// <summary>Total processor time of the process (all its threads), or <c>null</c> when it is gone.</summary>
    TimeSpan? TotalProcessorTime(int processId);
}

/// <summary>A plugin's processor use over the last samples.</summary>
public sealed record PluginCpuUse(string PluginUuid, double PercentOfOneCore, bool IsHeavy);

/// <summary>
/// The per-plugin CPU watchdog (T-31 a): plugins are third-party programs, and a busy loop in one of them makes the whole PC
/// feel slow while MasterHub looks innocent. Every few seconds the processor time of each running plugin is sampled; the
/// average over the last <see cref="Window"/> samples is the plugin's use in % of one core. Above
/// <see cref="HeavyPercent"/> the plugin is flagged in the UI (it is not stopped – the user decides); below
/// <see cref="ClearPercent"/> the flag goes away (hysteresis, so it does not blink).
/// </summary>
public sealed class PluginCpuWatchdog
{
    public const double HeavyPercent = 25;
    public const double ClearPercent = 15;
    public const int Window = 6;   // samples (≈ 30 s at one sample every 5 s)

    private readonly IProcessCpuSampler _sampler;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);

    private sealed class State
    {
        public int ProcessId;
        public TimeSpan LastCpu;
        public DateTime LastAt;
        public readonly Queue<double> Recent = new();
        public bool Heavy;
    }

    public PluginCpuWatchdog(IProcessCpuSampler sampler, Func<DateTime>? clock = null)
    {
        _sampler = sampler;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Takes one sample of every running plugin (uuid → process id) and returns the plugins whose flag changed. Plugins
    /// no longer running are forgotten.
    /// </summary>
    public IReadOnlyList<PluginCpuUse> Sample(IReadOnlyDictionary<string, int> running)
    {
        var changed = new List<PluginCpuUse>();
        DateTime now = _clock();

        foreach (string gone in _states.Keys.Where(k => !running.ContainsKey(k)).ToList())
        {
            if (_states[gone].Heavy) changed.Add(new PluginCpuUse(gone, 0, false));
            _states.Remove(gone);
        }

        foreach (var (uuid, pid) in running)
        {
            TimeSpan? cpu = _sampler.TotalProcessorTime(pid);
            if (cpu == null) continue;

            if (!_states.TryGetValue(uuid, out var s) || s.ProcessId != pid)
            {
                _states[uuid] = new State { ProcessId = pid, LastCpu = cpu.Value, LastAt = now };   // the first sample is the baseline
                continue;
            }

            double wall = (now - s.LastAt).TotalMilliseconds;
            if (wall <= 0) continue;
            double percent = Math.Max(0, (cpu.Value - s.LastCpu).TotalMilliseconds / wall * 100);
            s.LastCpu = cpu.Value;
            s.LastAt = now;
            s.Recent.Enqueue(percent);
            while (s.Recent.Count > Window) s.Recent.Dequeue();

            double average = s.Recent.Average();
            bool heavy = s.Heavy ? average >= ClearPercent : s.Recent.Count >= 2 && average >= HeavyPercent;
            if (heavy != s.Heavy)
            {
                s.Heavy = heavy;
                changed.Add(new PluginCpuUse(uuid, Math.Round(average, 1), heavy));
            }
        }
        return changed;
    }

    /// <summary>The current use of a plugin (0 when unknown).</summary>
    public PluginCpuUse Current(string uuid) =>
        _states.TryGetValue(uuid, out var s) && s.Recent.Count > 0
            ? new PluginCpuUse(uuid, Math.Round(s.Recent.Average(), 1), s.Heavy)
            : new PluginCpuUse(uuid, 0, false);
}

/// <summary>The real sampler (System.Diagnostics.Process).</summary>
public sealed class SystemProcessCpuSampler : IProcessCpuSampler
{
    public TimeSpan? TotalProcessorTime(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.HasExited ? null : process.TotalProcessorTime;
        }
        catch
        {
            return null;
        }
    }
}

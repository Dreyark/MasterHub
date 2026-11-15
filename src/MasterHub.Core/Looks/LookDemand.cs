using System;
using System.Collections.Generic;
using MasterHub.Core.Monitoring;

namespace MasterHub.Core.Looks;

/// <summary>What the conditional looks of the keys on screen need to be fed with (so the monitor samples only what is really used).</summary>
public readonly record struct LookDemandResult(MonitorMetrics Metrics, bool NowPlaying, bool Audio, bool Foreground, bool Clock, bool Status = false)
{
    public static LookDemandResult None => default;

    /// <summary>Anything at all to watch: the evaluator timer only runs when this is true.</summary>
    public bool Any => Metrics != MonitorMetrics.None || NowPlaying || Audio || Foreground || Clock || Status;

    public LookDemandResult Merge(LookDemandResult other) => new(
        Metrics | other.Metrics, NowPlaying | other.NowPlaying, Audio | other.Audio, Foreground | other.Foreground, Clock | other.Clock, Status | other.Status);
}

public static class LookDemand
{
    /// <summary>Demand of one key: its rules and every label (title/subtitle, also the rules' labels) that may carry tokens.</summary>
    public static LookDemandResult For(IReadOnlyList<KeyLookRule>? rules, params string?[] labels)
    {
        var result = LookDemandResult.None;

        if (rules != null)
        {
            foreach (var rule in rules)
            {
                if (!rule.Enabled) continue;
                result = result.Merge(ForCondition(rule.When.Kind));
                result = result.Merge(ForLabel(rule.Title)).Merge(ForLabel(rule.Subtitle));
            }
        }

        foreach (string? label in labels) result = result.Merge(ForLabel(label));
        return result;
    }

    public static LookDemandResult ForCondition(LookConditionKind kind) => kind switch
    {
        LookConditionKind.CpuAbove => new(MonitorMetrics.Cpu, false, false, false, false),
        LookConditionKind.RamAbove => new(MonitorMetrics.Ram, false, false, false, false),
        LookConditionKind.GpuAbove => new(MonitorMetrics.Gpu, false, false, false, false),
        // the CPU temperature falls back to the GPU one when there is no CPU sensor (same as the temperature key)
        LookConditionKind.CpuTempAbove => new(MonitorMetrics.CpuTemperature | MonitorMetrics.Gpu, false, false, false, false),
        LookConditionKind.GpuTempAbove => new(MonitorMetrics.Gpu, false, false, false, false),
        LookConditionKind.MusicPlaying => new(MonitorMetrics.None, true, false, false, false),
        LookConditionKind.MicMuted or LookConditionKind.MasterMuted => new(MonitorMetrics.None, false, true, false, false),
        LookConditionKind.AppInFront => new(MonitorMetrics.None, false, false, true, false),
        LookConditionKind.TimeBetween => new(MonitorMetrics.None, false, false, false, true),
        LookConditionKind.StatusWaiting or LookConditionKind.StatusWorking or LookConditionKind.StatusDone or LookConditionKind.StatusError
            => new(MonitorMetrics.None, false, false, false, false, true),
        _ => LookDemandResult.None
    };

    public static LookDemandResult ForLabel(string? label)
    {
        var result = LookDemandResult.None;
        foreach (string token in LabelTokens.Needed(label))
        {
            result = result.Merge(token switch
            {
                "cpu" => new(MonitorMetrics.Cpu, false, false, false, false),
                "ram" => new(MonitorMetrics.Ram, false, false, false, false),
                "gpu" or "gpu_temp" => new(MonitorMetrics.Gpu, false, false, false, false),
                "cpu_temp" => new(MonitorMetrics.CpuTemperature | MonitorMetrics.Gpu, false, false, false, false),
                "song" or "artist" => new(MonitorMetrics.None, true, false, false, false),
                "status" or "status_name" or "waiting" or "working" => new(MonitorMetrics.None, false, false, false, false, true),
                _ => LookDemandResult.None
            });
        }

        // {time} and {date} are not in Needed (they cost nothing) but they do need a periodic redraw
        if (LabelTokens.HasTokens(label) &&
            (label!.Contains("{time}", StringComparison.OrdinalIgnoreCase) || label.Contains("{date}", StringComparison.OrdinalIgnoreCase)))
            result = result.Merge(new(MonitorMetrics.None, false, false, false, true));

        return result;
    }
}

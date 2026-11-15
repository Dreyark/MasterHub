using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MasterHub.Core.Looks;

/// <summary>What a conditional look reacts to (T-12).</summary>
public enum LookConditionKind
{
    None = 0,
    CpuAbove = 1,
    RamAbove = 2,
    GpuAbove = 3,
    CpuTempAbove = 4,
    GpuTempAbove = 5,
    MusicPlaying = 6,
    MicMuted = 7,
    MasterMuted = 8,
    /// <summary>A program is in front (<see cref="KeyLookCondition.Text"/> = its name without path and <c>.exe</c>, case-insensitive).</summary>
    AppInFront = 9,
    /// <summary>Time of day, <see cref="KeyLookCondition.Text"/> = <c>HH:MM-HH:MM</c>; over midnight when the start is later than the end.</summary>
    TimeBetween = 10,
    /// <summary>Something a connected app reports waits for the user (T-25; Claude Code asks for permission, a build needs an answer).</summary>
    StatusWaiting = 11,
    StatusWorking = 12,
    /// <summary>Something finished (Claude Code is done with its turn).</summary>
    StatusDone = 13,
    StatusError = 14
}

public class KeyLookCondition
{
    public LookConditionKind Kind { get; set; } = LookConditionKind.None;

    /// <summary>Limit for the metric conditions (percent or °C).</summary>
    public double Threshold { get; set; } = 80;

    /// <summary>Program name or <c>HH:MM-HH:MM</c>.</summary>
    public string Text { get; set; } = "";
}

/// <summary>
/// One "When" rule of a key: a condition and the part of the look it changes. Rules are <b>layers, not first-match</b>:
/// every rule that holds lays the fields it sets over the key's look, a later rule wins; fields a rule leaves empty are not touched.
/// </summary>
public class KeyLookRule
{
    public bool Enabled { get; set; } = true;
    public KeyLookCondition When { get; set; } = new();
    public string? BackgroundColorHex { get; set; }
    public string? TextColorHex { get; set; }
    public string? SubtitleColorHex { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
}

/// <summary>What the conditions look at (the app implements it from the monitor, now-playing, audio and foreground services).</summary>
public interface ILookSignals
{
    double Cpu { get; }
    double Ram { get; }
    double Gpu { get; }
    double CpuTemp { get; }
    double GpuTemp { get; }
    bool MusicPlaying { get; }
    bool MicMuted { get; }
    bool MasterMuted { get; }
    string? ForegroundApp { get; }
    string? Song { get; }
    string? Artist { get; }
    DateTime Now { get; }

    // What the connected apps report (T-25). A default of "nothing" keeps every other implementation working.
    int StatusWaiting => 0;
    int StatusWorking => 0;
    int StatusDone => 0;
    int StatusError => 0;

    /// <summary>The state of the most urgent item (<c>waiting</c>, <c>error</c>, <c>done</c>, <c>working</c>, <c>idle</c>) or <c>null</c> when nothing reports.</summary>
    string? StatusTop => null;

    /// <summary>The name of the most urgent item (e.g. the project folder of the Claude Code session that needs you).</summary>
    string? StatusTopLabel => null;
}

/// <summary>The look that results from the rules that hold.</summary>
/// <param name="Signature">Which rules hold and the filled label: the picture only has to be redrawn when it changes.</param>
public sealed record AppliedLook(string? Background, string? Text, string? SubtitleColor, string? Title, string? Subtitle, string Signature)
{
    public static AppliedLook None { get; } = new(null, null, null, null, null, "");
    public bool IsNone => Signature.Length == 0;
}

/// <summary>Remembers which metric rules are "on" so a value hovering around its limit does not make the key flicker.</summary>
public sealed class KeyLookState
{
    private readonly HashSet<int> _latched = new();

    internal bool IsLatched(int rule) => _latched.Contains(rule);
    internal void Set(int rule, bool on) { if (on) _latched.Add(rule); else _latched.Remove(rule); }
    public void Reset() => _latched.Clear();
}

public static class KeyLookEvaluator
{
    /// <summary>A metric rule that came on stays on until the value is this far below its limit.</summary>
    public const double HysteresisMargin = 5;

    public static AppliedLook Evaluate(IReadOnlyList<KeyLookRule>? rules, ILookSignals signals, KeyLookState state)
    {
        if (rules == null || rules.Count == 0) return AppliedLook.None;

        string? bg = null, text = null, subColor = null, title = null, subtitle = null;
        var signature = new StringBuilder();

        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!rule.Enabled || !Holds(rule.When, signals, state, i)) continue;

            signature.Append('r').Append(i).Append(';');
            bg = Set(rule.BackgroundColorHex) ?? bg;
            text = Set(rule.TextColorHex) ?? text;
            subColor = Set(rule.SubtitleColorHex) ?? subColor;
            title = Set(rule.Title) ?? title;
            subtitle = Set(rule.Subtitle) ?? subtitle;
        }

        if (signature.Length == 0) return AppliedLook.None;

        // label tokens ("{cpu}") are filled now, and the filled text is part of the signature
        title = title == null ? null : LabelTokens.Substitute(title, signals);
        subtitle = subtitle == null ? null : LabelTokens.Substitute(subtitle, signals);
        // the colours are part of the signature too: editing a rule's colour must count as a change
        signature.Append('|').Append(title).Append('|').Append(subtitle).Append('|').Append(bg).Append(text).Append(subColor);

        return new AppliedLook(bg, text, subColor, title, subtitle, signature.ToString());
    }

    private static string? Set(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public static bool Holds(KeyLookCondition c, ILookSignals s, KeyLookState state, int ruleIndex)
    {
        switch (c.Kind)
        {
            case LookConditionKind.CpuAbove: return Metric(s.Cpu, c.Threshold, state, ruleIndex);
            case LookConditionKind.RamAbove: return Metric(s.Ram, c.Threshold, state, ruleIndex);
            case LookConditionKind.GpuAbove: return Metric(s.Gpu, c.Threshold, state, ruleIndex);
            case LookConditionKind.CpuTempAbove: return Metric(s.CpuTemp, c.Threshold, state, ruleIndex);
            case LookConditionKind.GpuTempAbove: return Metric(s.GpuTemp, c.Threshold, state, ruleIndex);
            case LookConditionKind.MusicPlaying: return s.MusicPlaying;
            case LookConditionKind.MicMuted: return s.MicMuted;
            case LookConditionKind.MasterMuted: return s.MasterMuted;
            case LookConditionKind.AppInFront: return AppMatches(s.ForegroundApp, c.Text);
            case LookConditionKind.TimeBetween: return InTimeRange(s.Now, c.Text);
            case LookConditionKind.StatusWaiting: return s.StatusWaiting > 0;
            case LookConditionKind.StatusWorking: return s.StatusWorking > 0;
            case LookConditionKind.StatusDone: return s.StatusDone > 0;
            case LookConditionKind.StatusError: return s.StatusError > 0;
            default: return false;
        }
    }

    private static bool Metric(double value, double limit, KeyLookState state, int ruleIndex)
    {
        bool on = state.IsLatched(ruleIndex) ? value > limit - HysteresisMargin : value >= limit;
        state.Set(ruleIndex, on);
        return on;
    }

    /// <summary>Program names are compared without path and <c>.exe</c>, ignoring case.</summary>
    public static bool AppMatches(string? foreground, string wanted)
    {
        string Clean(string? n)
        {
            string name = System.IO.Path.GetFileName((n ?? "").Trim());
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }

        string w = Clean(wanted);
        return w.Length > 0 && string.Equals(Clean(foreground), w, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>HH:MM-HH:MM</c>; when the start is later than the end the range runs over midnight (22:00-06:00).</summary>
    public static bool InTimeRange(DateTime now, string range)
    {
        var parts = (range ?? "").Split('-');
        if (parts.Length != 2 || !TimeSpan.TryParseExact(parts[0].Trim(), "h\\:mm", CultureInfo.InvariantCulture, out var from) ||
            !TimeSpan.TryParseExact(parts[1].Trim(), "h\\:mm", CultureInfo.InvariantCulture, out var to)) return false;

        var t = now.TimeOfDay;
        return from <= to ? t >= from && t < to : t >= from || t < to;
    }
}

/// <summary>Substitutes <c>{cpu} {ram} {gpu} {cpu_temp} {gpu_temp} {time} {date} {song} {artist}</c> and the status tokens <c>{status} {status_name} {waiting} {working}</c> in a label.</summary>
public static class LabelTokens
{
    public static bool HasTokens(string? text) => text != null && text.Contains('{') && text.Contains('}');

    public static string Substitute(string text, ILookSignals s)
    {
        if (!HasTokens(text)) return text;

        return text
            .Replace("{cpu_temp}", $"{Math.Round(s.CpuTemp)}°C", StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_temp}", $"{Math.Round(s.GpuTemp)}°C", StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu}", $"{Math.Round(s.Cpu)}%", StringComparison.OrdinalIgnoreCase)
            .Replace("{ram}", $"{Math.Round(s.Ram)}%", StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu}", $"{Math.Round(s.Gpu)}%", StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", s.Now.ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", s.Now.ToString("dd MMM", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{song}", s.Song ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{artist}", s.Artist ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{status_name}", s.StatusTopLabel ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{status}", s.StatusTop ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{waiting}", s.StatusWaiting.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{working}", s.StatusWorking.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The monitor groups a label needs (so a token turns the right sampling on, F-36).</summary>
    public static IEnumerable<string> Needed(string? text)
    {
        if (!HasTokens(text)) yield break;
        foreach (string token in new[] { "cpu", "ram", "gpu", "cpu_temp", "gpu_temp", "song", "artist", "status", "status_name", "waiting", "working" })
            if (text!.Contains("{" + token + "}", StringComparison.OrdinalIgnoreCase)) yield return token;
    }
}

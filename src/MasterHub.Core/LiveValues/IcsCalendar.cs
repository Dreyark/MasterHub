using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MasterHub.Core.LiveValues;

/// <summary>One event (or recurrence master) read from an ICS calendar.</summary>
public sealed class IcsEvent
{
    public string Uid { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime Start { get; set; }          // wall-clock time in <see cref="Zone"/>
    public DateTime End { get; set; }
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Local;
    public bool AllDay { get; set; }
    public bool Cancelled { get; set; }
    public string? RRule { get; set; }
    public List<DateTime> ExDates { get; } = new();

    /// <summary>For an override of one occurrence: the original start of the occurrence it replaces.</summary>
    public DateTime? RecurrenceId { get; set; }

    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
    public DateTimeOffset StartAt => ToOffset(Start);
    public DateTimeOffset ToOffset(DateTime wall) => new(wall, Zone.GetUtcOffset(wall));
}

/// <summary>A small ICS reader: single and repeating events, exceptions, moved and cancelled occurrences; all-day events are skipped.</summary>
public static class IcsParser
{
    public static List<IcsEvent> Parse(string ics)
    {
        var events = new List<IcsEvent>();
        IcsEvent? current = null;
        bool hasEnd = false;
        TimeSpan? duration = null;

        foreach (var (name, parameters, value) in Lines(ics))
        {
            switch (name)
            {
                case "BEGIN" when value == "VEVENT":
                    current = new IcsEvent();
                    hasEnd = false;
                    duration = null;
                    break;
                case "END" when value == "VEVENT" && current != null:
                    if (!hasEnd) current.End = current.Start + (duration ?? (current.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero));
                    events.Add(current);
                    current = null;
                    break;
            }

            if (current == null) continue;

            switch (name)
            {
                case "UID": current.Uid = value; break;
                case "SUMMARY": current.Summary = Unescape(value); break;
                case "STATUS": current.Cancelled = value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase); break;
                case "RRULE": current.RRule = value; break;
                case "DTSTART":
                    (current.Start, current.Zone, current.AllDay) = ParseDate(value, parameters);
                    break;
                case "DTEND":
                    current.End = ParseDate(value, parameters).Wall;
                    hasEnd = true;
                    break;
                case "DURATION": duration = ParseDuration(value); break;
                case "EXDATE":
                    foreach (string part in value.Split(',')) current.ExDates.Add(ParseDate(part, parameters).Wall);
                    break;
                case "RECURRENCE-ID": current.RecurrenceId = ParseDate(value, parameters).Wall; break;
            }
        }

        return events.Where(e => e.Start != default).ToList();
    }

    // Unfolds continuation lines and splits "NAME;PARAM=x:value".
    private static IEnumerable<(string Name, Dictionary<string, string> Params, string Value)> Lines(string ics)
    {
        var unfolded = new List<string>();
        foreach (string raw in (ics ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && unfolded.Count > 0) unfolded[^1] += raw[1..];
            else unfolded.Add(raw);
        }

        foreach (string line in unfolded)
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var head = line[..colon].Split(';');
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in head.Skip(1))
            {
                int eq = p.IndexOf('=');
                if (eq > 0) parameters[p[..eq]] = p[(eq + 1)..].Trim('"');
            }
            yield return (head[0].ToUpperInvariant(), parameters, line[(colon + 1)..].Trim());
        }
    }

    private static (DateTime Wall, TimeZoneInfo Zone, bool AllDay) ParseDate(string value, Dictionary<string, string> parameters)
    {
        value = value.Trim();
        bool allDay = parameters.TryGetValue("VALUE", out var v) && v.Equals("DATE", StringComparison.OrdinalIgnoreCase) || value.Length == 8;
        if (allDay)
        {
            return DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? (d, TimeZoneInfo.Local, true)
                : (default, TimeZoneInfo.Local, true);
        }

        bool utc = value.EndsWith('Z');
        string digits = value.TrimEnd('Z');
        if (!DateTime.TryParseExact(digits, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
            return (default, TimeZoneInfo.Local, false);

        if (utc) return (TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(wall, DateTimeKind.Utc), TimeZoneInfo.Local), TimeZoneInfo.Local, false);

        var zone = TimeZoneInfo.Local;
        if (parameters.TryGetValue("TZID", out var tzid))
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(tzid); } catch { /* unknown zone: treat as local time */ }
        }
        return (wall, zone, false);
    }

    private static TimeSpan? ParseDuration(string value)
    {
        // PT1H30M, P1D, PT45M
        var m = System.Text.RegularExpressions.Regex.Match(value, @"^P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
        if (!m.Success) return null;
        int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
        return TimeSpan.FromDays(G(1) * 7 + G(2)) + TimeSpan.FromHours(G(3)) + TimeSpan.FromMinutes(G(4)) + TimeSpan.FromSeconds(G(5));
    }

    private static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}

/// <summary>The next (or running) event of a calendar.</summary>
public sealed record IcsNext(IcsEvent Event, DateTimeOffset Start, DateTimeOffset End)
{
    public bool IsRunning(DateTimeOffset now) => Start <= now && now < End;
}

public static class IcsSchedule
{
    private const int MaxSteps = 20000;

    /// <summary>The running event if there is one, otherwise the nearest upcoming one within <paramref name="horizon"/>.</summary>
    public static IcsNext? Next(IReadOnlyList<IcsEvent> events, DateTimeOffset now, TimeSpan horizon)
    {
        IcsNext? best = null;
        var overrides = events.Where(e => e.RecurrenceId != null).ToList();

        foreach (var master in events.Where(e => e.RecurrenceId == null && !e.AllDay))
        {
            foreach (var wall in Occurrences(master, now - master.Duration - TimeSpan.FromDays(1), now + horizon))
            {
                if (master.ExDates.Contains(wall)) continue;

                var source = master;
                var replaced = overrides.FirstOrDefault(o => o.Uid == master.Uid && o.RecurrenceId == wall);
                if (replaced != null)
                {
                    if (replaced.Cancelled || replaced.AllDay) continue;
                    source = replaced; // a moved / renamed occurrence
                }
                else if (master.Cancelled)
                {
                    continue;
                }

                var start = source == master ? master.ToOffset(wall) : source.StartAt;
                var end = start + source.Duration;
                if (end <= now) continue; // already over

                if (best == null || start < best.Start) best = new IcsNext(source, start, end);
            }
        }

        // an override that moved an occurrence into the window although its original start lies outside of it
        foreach (var o in overrides.Where(o => !o.Cancelled && !o.AllDay && !events.Any(m => m.RecurrenceId == null && m.Uid == o.Uid)))
        {
            var start = o.StartAt;
            if (start + o.Duration > now && start <= now + horizon && (best == null || start < best.Start)) best = new IcsNext(o, start, start + o.Duration);
        }

        return best;
    }

    /// <summary>Wall-clock starts of an event (and of its repeats) that fall in [from, to].</summary>
    public static IEnumerable<DateTime> Occurrences(IcsEvent e, DateTimeOffset from, DateTimeOffset to)
    {
        if (string.IsNullOrEmpty(e.RRule))
        {
            var at = e.StartAt;
            if (at >= from && at <= to) yield return e.Start;
            yield break;
        }

        var rule = ParseRule(e.RRule);
        int interval = rule.TryGetValue("INTERVAL", out var intervalText) && int.TryParse(intervalText, out int iv) ? Math.Max(1, iv) : 1;
        int count = rule.TryGetValue("COUNT", out var c) && int.TryParse(c, out int cn) ? cn : int.MaxValue;
        DateTime? until = rule.TryGetValue("UNTIL", out var u) ? ParseUntil(u, e.Zone) : null;
        var days = rule.TryGetValue("BYDAY", out var by) ? ParseDays(by) : new List<DayOfWeek>();

        int produced = 0;
        DateTime anchor = e.Start;
        for (int step = 0; step < MaxSteps; step++)
        {
            IEnumerable<DateTime> candidates = rule.GetValueOrDefault("FREQ", "") switch
            {
                "DAILY" => new[] { anchor.AddDays(step * interval) },
                "WEEKLY" => WeekCandidates(anchor, step * interval, days),
                "MONTHLY" => new[] { anchor.AddMonths(step * interval) },
                "YEARLY" => new[] { anchor.AddYears(step * interval) },
                _ => Array.Empty<DateTime>()
            };

            bool any = false;
            foreach (var wall in candidates)
            {
                if (wall < e.Start) continue;
                any = true;
                if (until != null && wall > until) yield break;
                if (++produced > count) yield break;

                var at = e.ToOffset(wall);
                if (at > to) yield break;
                if (at >= from) yield return wall;
            }

            if (!any && rule.GetValueOrDefault("FREQ", "") is not ("WEEKLY")) yield break;
        }
    }

    private static IEnumerable<DateTime> WeekCandidates(DateTime anchor, int weekOffset, List<DayOfWeek> days)
    {
        if (days.Count == 0)
        {
            yield return anchor.AddDays(weekOffset * 7);
            yield break;
        }

        // the Monday of the anchor's week, shifted by whole weeks, then each wanted weekday
        int back = ((int)anchor.DayOfWeek + 6) % 7;
        var monday = anchor.Date.AddDays(-back).AddDays(weekOffset * 7);
        foreach (var day in days.OrderBy(d => ((int)d + 6) % 7))
        {
            yield return monday.AddDays(((int)day + 6) % 7).Add(anchor.TimeOfDay);
        }
    }

    private static Dictionary<string, string> ParseRule(string rrule)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in rrule.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) map[part[..eq].ToUpperInvariant()] = part[(eq + 1)..];
        }
        return map;
    }

    private static List<DayOfWeek> ParseDays(string by)
    {
        var result = new List<DayOfWeek>();
        foreach (string token in by.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string d = new string(token.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            DayOfWeek? day = d switch
            {
                "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
                "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null
            };
            if (day != null) result.Add(day.Value);
        }
        return result;
    }

    private static DateTime? ParseUntil(string value, TimeZoneInfo zone)
    {
        value = value.Trim();
        bool utc = value.EndsWith('Z');
        string fmt = value.TrimEnd('Z').Contains('T') ? "yyyyMMdd'T'HHmmss" : "yyyyMMdd";
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
        if (fmt == "yyyyMMdd") return d.AddDays(1).AddTicks(-1);
        return utc ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d, DateTimeKind.Utc), zone) : d;
    }

    /// <summary>"Now", "12 min", "2 h 5 min", "3 d" – compact and language-neutral.</summary>
    public static string Describe(TimeSpan untilStart, bool running)
    {
        if (running || untilStart <= TimeSpan.Zero) return "Now";
        if (untilStart.TotalMinutes < 1) return "<1 min";
        if (untilStart.TotalHours < 1) return $"{(int)Math.Ceiling(untilStart.TotalMinutes)} min";
        if (untilStart.TotalHours < 24)
        {
            int h = (int)untilStart.TotalHours;
            int m = untilStart.Minutes;
            return m == 0 ? $"{h} h" : $"{h} h {m} min";
        }
        return $"{(int)untilStart.TotalDays} d";
    }
}

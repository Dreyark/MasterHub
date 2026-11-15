using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MasterHub.Core.LiveValues;

/// <summary>Where a live value comes from (T-11).</summary>
public enum LiveValueSourceKind
{
    /// <summary>Not configured yet (the key shows "Set up").</summary>
    None = 0,
    /// <summary>An HTTP address returning plain text or JSON (optionally a JSON path).</summary>
    WebAddress = 1,
    /// <summary>Current weather (Open-Meteo, no key).</summary>
    Weather = 2,
    /// <summary>Time to the next event of a private calendar (ICS URL).</summary>
    CalendarNext = 3,
    /// <summary>State of a Home Assistant entity.</summary>
    HomeAssistant = 4,
    /// <summary>First output line of a command (needs approval of the exact command line).</summary>
    Command = 5
}

/// <summary>Which direction of change is bad (selects how the thresholds are compared).</summary>
public enum WorseWhen
{
    Higher = 0,
    Lower = 1
}

/// <summary>How serious a value is: drives the colour of the key.</summary>
public enum LiveSeverity
{
    Normal = 0,
    Warning = 1,
    Alert = 2
}

/// <summary>What a reading currently is (the key shows "Set up", "…", "–" or "Allow?" for the non-<see cref="Ok"/> ones).</summary>
public enum LiveReadingState
{
    SetUp,
    Loading,
    Ok,
    Error,
    NeedsApproval
}

/// <summary>
/// The configuration of a live-value key as stored in the profile. Secrets are <b>not</b> in here: a Bearer token lives in
/// the DPAPI token store under <see cref="TokenName"/>, so profiles and exports never carry it.
/// </summary>
public class LiveValueSettings
{
    public LiveValueSourceKind Kind { get; set; } = LiveValueSourceKind.None;

    /// <summary>Web address, or the ICS URL (<c>webcal://</c> is rewritten to <c>https://</c>).</summary>
    public string Url { get; set; } = "";

    /// <summary>JSON path such as <c>data.items.0.price</c> or <c>data.items[0].price</c>; empty = plain text.</summary>
    public string JsonPath { get; set; } = "";

    /// <summary>Name of the stored Bearer token (not the token).</summary>
    public string TokenName { get; set; } = "";

    /// <summary>A city name, or <c>Name|lat|lon</c> once it was resolved.</summary>
    public string City { get; set; } = "";

    public string EntityId { get; set; } = "";
    public string Command { get; set; } = "";

    /// <summary>Seconds between fetches (minimum 5).</summary>
    public int RefreshSeconds { get; set; } = 60;

    public string Before { get; set; } = "";
    public string After { get; set; } = "";

    /// <summary>Decimals shown for numbers; <c>-1</c> = the value as it came.</summary>
    public int Decimals { get; set; } = -1;

    public double? WarnAt { get; set; }
    public double? AlertAt { get; set; }
    public WorseWhen Worse { get; set; } = WorseWhen.Higher;

    /// <summary>The largest sensible value (a dial draws a ring up to it).</summary>
    public double? Maximum { get; set; }

    public bool IsConfigured => Kind switch
    {
        LiveValueSourceKind.WebAddress or LiveValueSourceKind.CalendarNext => !string.IsNullOrWhiteSpace(Url),
        LiveValueSourceKind.Weather => !string.IsNullOrWhiteSpace(City),
        LiveValueSourceKind.HomeAssistant => !string.IsNullOrWhiteSpace(EntityId),
        LiveValueSourceKind.Command => !string.IsNullOrWhiteSpace(Command),
        _ => false
    };

    /// <summary>
    /// Identity of what has to be <b>fetched</b>: keys with the same source share one fetch (formatting and thresholds are
    /// per key). Nothing secret goes into it (the token is represented by its name).
    /// </summary>
    public string SourceId()
    {
        string raw = string.Join("\u001f", (int)Kind, Url.Trim(), JsonPath.Trim(), TokenName.Trim(), City.Trim(), EntityId.Trim(), Command.Trim());
        return "live:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
    }

    /// <summary>Seconds between fetches, never below the 5 s floor (the cap) and longer for weather (10 min).</summary>
    public int EffectiveRefreshSeconds => Kind switch
    {
        LiveValueSourceKind.Weather => Math.Max(600, RefreshSeconds),
        LiveValueSourceKind.CalendarNext => Math.Max(5, RefreshSeconds),
        _ => Math.Max(5, RefreshSeconds)
    };
}

/// <summary>The raw result of a fetch (formatting and thresholds are applied per key by <see cref="LiveValueFormatter"/>).</summary>
/// <param name="State">What the reading is.</param>
/// <param name="Raw">The value as text.</param>
/// <param name="Numeric">The value as a number when it is one (thresholds and decimals use it).</param>
/// <param name="Caption">Short explanation (weather word, event title).</param>
public sealed record LiveReading(LiveReadingState State, string? Raw = null, double? Numeric = null, string? Caption = null)
{
    public static LiveReading Loading { get; } = new(LiveReadingState.Loading);
    public static LiveReading SetUp { get; } = new(LiveReadingState.SetUp);
    public static LiveReading Error { get; } = new(LiveReadingState.Error);
    public static LiveReading NeedsApproval { get; } = new(LiveReadingState.NeedsApproval);
}

/// <summary>What a key shows for a reading.</summary>
public sealed record LiveDisplay(string Text, LiveSeverity Severity, double? Percent);

/// <summary>Reads a value out of JSON by path (<c>a.b.0.c</c> and <c>a.b[0].c</c> both work).</summary>
public static class JsonPathReader
{
    /// <summary>The value at <paramref name="path"/> as text, or <c>null</c> when the path leads nowhere or the JSON is invalid.</summary>
    public static string? Read(string json, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return json?.Trim();

        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return null; }

        foreach (string segment in Split(path))
        {
            if (node is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out node))
                {
                    // property names are matched case-insensitively as a convenience
                    node = obj.FirstOrDefault(p => string.Equals(p.Key, segment, StringComparison.OrdinalIgnoreCase)).Value;
                }
            }
            else if (node is JsonArray arr && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                node = index < arr.Count ? arr[index] : null;
            }
            else
            {
                return null;
            }

            if (node == null) return null;
        }

        return node switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString().Trim('"'),
            _ => node!.ToJsonString()
        };
    }

    private static IEnumerable<string> Split(string path)
    {
        // "a.items[0].price" -> a, items, 0, price
        foreach (string part in path.Replace("[", ".").Replace("]", "").Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return part;
    }
}

/// <summary>Turns a reading into what the key shows: Before/After/Decimals, thresholds and the dial percentage.</summary>
public static class LiveValueFormatter
{
    public static LiveDisplay Render(LiveReading reading, LiveValueSettings settings)
    {
        switch (reading.State)
        {
            case LiveReadingState.SetUp: return new("Set up", LiveSeverity.Normal, null);
            case LiveReadingState.Loading: return new("…", LiveSeverity.Normal, null);
            case LiveReadingState.NeedsApproval: return new("Allow?", LiveSeverity.Normal, null);
            case LiveReadingState.Error: return new("–" + (string.IsNullOrWhiteSpace(reading.Caption) ? "" : " " + reading.Caption), LiveSeverity.Normal, null);
        }

        double? number = reading.Numeric ?? TryNumber(reading.Raw);
        string text = reading.Raw ?? "";
        // Decimals only apply to a value that is itself a number ("12.3456"); "18°" or "12 min" are shown as they are.
        if (TryNumber(reading.Raw) is double shown && settings.Decimals >= 0)
        {
            text = shown.ToString("F" + Math.Min(settings.Decimals, 10), CultureInfo.InvariantCulture);
        }

        text = settings.Before + text + settings.After;
        if (!string.IsNullOrWhiteSpace(reading.Caption)) text += " " + reading.Caption;

        double? percent = number is double v && settings.Maximum is > 0 ? Math.Clamp(v / settings.Maximum.Value, 0, 1) : null;
        return new(text, number is double sev ? Evaluate(sev, settings) : LiveSeverity.Normal, percent);
    }

    /// <summary>Warning / alert by the thresholds in the direction given by <see cref="LiveValueSettings.Worse"/>.</summary>
    public static LiveSeverity Evaluate(double value, LiveValueSettings s)
    {
        bool Reached(double? limit) => limit is double l && (s.Worse == WorseWhen.Higher ? value >= l : value <= l);
        if (Reached(s.AlertAt)) return LiveSeverity.Alert;
        if (Reached(s.WarnAt)) return LiveSeverity.Warning;
        return LiveSeverity.Normal;
    }

    public static double? TryNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }
}

using System;
using System.Globalization;
using System.Text.Json;

namespace MasterHub.Core.LiveValues;

/// <summary>A place for the weather: name and coordinates, stored as <c>Name|lat|lon</c>.</summary>
public sealed record WeatherPlace(string Name, double Latitude, double Longitude)
{
    public string Format() => string.Create(CultureInfo.InvariantCulture, $"{Name}|{Latitude:0.####}|{Longitude:0.####}");

    /// <summary>Reads <c>Name|lat|lon</c>; <c>null</c> for a plain city name (which still has to be looked up).</summary>
    public static WeatherPlace? Parse(string? text)
    {
        var parts = (text ?? "").Split('|');
        if (parts.Length != 3) return null;
        return double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
               double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) &&
               lat is >= -90 and <= 90 && lon is >= -180 and <= 180
            ? new WeatherPlace(parts[0].Trim(), lat, lon)
            : null;
    }
}

/// <summary>Open-Meteo (free, no key): the forecast for the current temperature and a short word, and the geocoding search.</summary>
public static class OpenMeteo
{
    public static string ForecastUrl(WeatherPlace place) => string.Create(CultureInfo.InvariantCulture,
        $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude:0.####}&longitude={place.Longitude:0.####}&current=temperature_2m,weather_code&timezone=auto");

    public static string GeocodingUrl(string city) =>
        $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&language=en&format=json";

    /// <summary>Temperature (whole degrees) and the weather word from a forecast answer; <c>null</c> when the answer is not usable.</summary>
    public static (double Temperature, string Word)? ParseForecast(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("current", out var current)) return null;
            if (!current.TryGetProperty("temperature_2m", out var t) || t.ValueKind != JsonValueKind.Number) return null;
            int code = current.TryGetProperty("weather_code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
            return (Math.Round(t.GetDouble()), WordFor(code));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static WeatherPlace? ParseGeocoding(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return null;

            var first = results[0];
            string name = first.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!first.TryGetProperty("latitude", out var lat) || !first.TryGetProperty("longitude", out var lon)) return null;
            return new WeatherPlace(name.Replace("|", " "), lat.GetDouble(), lon.GetDouble());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>WMO weather code → one short word.</summary>
    public static string WordFor(int code) => code switch
    {
        0 => "Clear",
        1 or 2 => "Partly cloudy",
        3 => "Cloudy",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers",
        85 or 86 => "Snow showers",
        >= 95 and <= 99 => "Storm",
        _ => ""
    };
}

using System;
using System.Collections.Generic;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Pomocnik do obsługi zaawansowanej palety barw, konwersji HSV/RGB oraz predefiniowanych kolorów studio.
/// </summary>
public static class ColorStudioHelper
{
    public record ColorItem(string Hex, string Name);

    public static readonly IReadOnlyList<ColorItem> CuratedPalette = new List<ColorItem>
    {
        // Wiersz 1: Cybernetyczne i chłodne (Cyan, Blue, Indigo, Purple)
        new("#00F0B5", "Cyber Turkus"),
        new("#00FFCC", "Neonowy Turkus"),
        new("#06B6D4", "Morski Akwamaryn"),
        new("#38BDF8", "Błękit Nieba"),
        new("#3B82F6", "Błękit Królewski"),
        new("#6366F1", "Elektryczne Indygo"),
        new("#8B5CF6", "Ametystowy Fiolet"),
        new("#A855F7", "Głęboka Purpura"),

        // Wiersz 2: Ciepłe, ogniste i zielenie (Pink, Red, Orange, Amber, Gold, Lime, Emerald, Mint)
        new("#EC4899", "Neonowy Róż"),
        new("#EF4444", "Ognista Czerwień"),
        new("#F97316", "Koralowy Pomarańcz"),
        new("#F59E0B", "Ciepły Bursztyn"),
        new("#FACC15", "Czyste Złoto"),
        new("#84CC16", "Limonkowa Zieleń"),
        new("#10B981", "Szmaragd"),
        new("#059669", "Ciemna Mięta"),

        // Wiersz 3: Pastele, biele i neutralne
        new("#A7F3D0", "Pastelowa Mięta"),
        new("#BAE6FD", "Pastelowy Błękit"),
        new("#DDD6FE", "Pastelowa Lawenda"),
        new("#FBCFE8", "Pastelowy Róż"),
        new("#FEF08A", "Pastelowy Żółty"),
        new("#FFFFFF", "Czysta Biel"),
        new("#CBD5E1", "Zimna Biel 6500K"),
        new("#1E293B", "Ciemny Grafit")
    };

    /// <summary>
    /// Konwertuje model barw HSV na wartości RGB (0..255).
    /// </summary>
    public static (byte r, byte g, byte b) HsvToRgb(double hue, double saturation, double value)
    {
        hue = Math.Clamp(hue, 0, 360);
        if (hue >= 360) hue = 0;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        int hi = Convert.ToInt32(Math.Floor(hue / 60.0)) % 6;
        double f = hue / 60.0 - Math.Floor(hue / 60.0);

        double v = value * 255.0;
        byte vVal = (byte)Math.Clamp(v, 0, 255);
        byte pVal = (byte)Math.Clamp(v * (1.0 - saturation), 0, 255);
        byte qVal = (byte)Math.Clamp(v * (1.0 - f * saturation), 0, 255);
        byte tVal = (byte)Math.Clamp(v * (1.0 - (1.0 - f) * saturation), 0, 255);

        return hi switch
        {
            0 => (vVal, tVal, pVal),
            1 => (qVal, vVal, pVal),
            2 => (pVal, vVal, tVal),
            3 => (pVal, qVal, vVal),
            4 => (tVal, pVal, vVal),
            _ => (vVal, pVal, qVal),
        };
    }

    /// <summary>
    /// Konwertuje wartości RGB na model barw HSV (Hue: 0..360, Saturation: 0..1, Value: 0..1).
    /// </summary>
    public static (double h, double s, double v) RgbToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0;
        if (delta > 0.00001)
        {
            if (Math.Abs(max - rd) < 0.00001)
                h = ((gd - bd) / delta) % 6;
            else if (Math.Abs(max - gd) < 0.00001)
                h = (bd - rd) / delta + 2;
            else
                h = (rd - gd) / delta + 4;

            h *= 60.0;
            if (h < 0) h += 360.0;
        }

        double s = max > 0.00001 ? delta / max : 0;
        double v = max;

        return (h, s, v);
    }

    /// <summary>
    /// Dokonuje sprzętowej kalibracji gamma (sRGB -> przestrzeń liniowa PWM dla diod LED, domyślnie gamma ~2.2)
    /// z uwzględnieniem poziomu jasności (0..100%).
    /// Pozwala na wierne odwzorowanie barw z ekranu na fizycznych diodach LED Cooler Master MasterHub
    /// (eliminuje efekt wyblakłych, zbyt jasnych odcieni zieleni, zażółconego pomarańczu i bladego fioletu).
    /// </summary>
    public static (byte r, byte g, byte b) CalibrateForHardwareLed(byte r, byte g, byte b, byte brightnessPercent = 100, double gamma = 2.2)
    {
        if (brightnessPercent == 0 || (r == 0 && g == 0 && b == 0))
            return (0, 0, 0);

        double bFactor = Math.Clamp(brightnessPercent, (byte)0, (byte)100) / 100.0;

        // Skalowanie w przestrzeni percepcyjnej
        double rn = (r / 255.0) * bFactor;
        double gn = (g / 255.0) * bFactor;
        double bn = (b / 255.0) * bFactor;

        // Przeliczenie na krzywą gamma PWM
        byte rOut = (byte)Math.Clamp(Math.Round(Math.Pow(rn, gamma) * 255.0), 0, 255);
        byte gOut = (byte)Math.Clamp(Math.Round(Math.Pow(gn, gamma) * 255.0), 0, 255);
        byte bOut = (byte)Math.Clamp(Math.Round(Math.Pow(bn, gamma) * 255.0), 0, 255);

        // Zachowaj minimalne świecenie dla niezerowych kanałów przy aktywnej jasności
        if (r > 0 && rOut == 0 && bFactor > 0.05) rOut = 1;
        if (g > 0 && gOut == 0 && bFactor > 0.05) gOut = 1;
        if (b > 0 && bOut == 0 && bFactor > 0.05) bOut = 1;

        return (rOut, gOut, bOut);
    }
}


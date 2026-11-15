using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MasterHub.Avalonia.Views.Controls;

/// <summary>
/// Klucz ikony (np. "Volume") -> geometria zasobu "IconVolume" z Styles/Icons.axaml.
/// Jedna rodzina ikon wektorowych zamiast kolorowych emoji.
/// </summary>
public static class IconConverters
{
    public static readonly IValueConverter ByKey = new ByKeyConverter();

    /// <summary>DeckActionType -> geometria ikony.</summary>
    public static readonly IValueConverter ByAction = new ByActionConverter();

    private sealed class ByActionConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => ByKey.Convert(value is MasterHub.Core.Actions.DeckActionType a ? ForAction(a) : "Computer", targetType, parameter, culture);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }

    /// <summary>Kategoria akcji (klucz lokalizacji Cat_*) -> klucz ikony.</summary>
    private static readonly Dictionary<string, string> CategoryIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cat_System"] = "Computer", ["Cat_Audio"] = "Volume", ["Cat_MainAudio"] = "Volume", ["Cat_Media"] = "Play",
        ["Cat_Spotify"] = "Spotify", ["Cat_Vlc"] = "Vlc", ["Cat_Voicemod"] = "Voicemod", ["Cat_Monitor"] = "Speed",
        ["Cat_OBS"] = "ObsLogo", ["Cat_Twitch"] = "Twitch", ["Cat_Kick"] = "Kick", ["Cat_JetKvm"] = "JetKvm",
        ["Cat_Streaming"] = "Cast", ["Cat_Discord"] = "Discord", ["Cat_Teams"] = "Teams", ["Cat_Macros"] = "Bolt",
        ["Cat_Razer"] = "Razer", ["Cat_Wooting"] = "Keyboard", ["Cat_Nav"] = "Compass", ["Cat_Applications"] = "Apps",
        ["Cat_SmartHome"] = "HomeAssistant"
    };

    /// <summary>Akcja decka -> klucz ikony (po nazwie typu; spójnie z kategoriami biblioteki akcji).</summary>
    public static string ForAction(MasterHub.Core.Actions.DeckActionType a)
    {
        string n = a.ToString();
        // Kolejność ma znaczenie: pierwsze trafienie wygrywa (konkretne słowa przed prefiksami platform).
        (string Token, string Icon)[] rules =
        {
            ("AudioOutput", "Headphones"), ("Mute", "Mute"), ("Deafen", "Headphones"), ("Hotkey", "Keyboard"), ("TextPaste", "Keyboard"),
            ("Clipboard", "Folder"), ("Record", "Video"), ("Camera", "Video"), ("VirtualCam", "Video"), ("Clip", "Video"), ("Stream", "Cast"), ("ScreenShare", "Cast"),
            ("Scene", "Image"), ("Screenshot", "Image"), ("Screensaver", "Image"),
            ("Shutdown", "Power"), ("Restart", "Power"), ("Sleep", "Standby"), ("LockScreen", "Key"), ("Delay", "Clock"),
            ("Clock", "Clock"), ("Sound", "Volume"), ("Timer", "Clock"), ("Pomodoro", "Clock"), ("Stopwatch", "Clock"), ("Date", "Clock"), ("Desktop", "Studio"), ("TaskView", "Studio"),
            ("TaskManager", "Speed"), ("Calculator", "Apps"), ("Browser", "Globe"), ("Website", "Globe"), ("OpenApplication", "Apps"),
            ("CloseApplication", "Close"), ("OpenFile", "Folder"), ("Folder", "Folder"),
            ("Obs", "ObsLogo"), ("Twitch", "Twitch"), ("Kick", "Kick"), ("Discord", "Discord"), ("Teams", "Teams"),
            ("Spotify", "Spotify"), ("Vlc", "Vlc"), ("Voicemod", "Voicemod"), ("JetKvm", "JetKvm"),
            ("Dpi", "Target"), ("PollingRate", "Speed"), ("Profile", "Profiles"), ("Razer", "Razer"), ("Wooting", "Keyboard"),
            ("Display", "Speed"), ("LiveValue", "Speed"), ("Fps", "Speed"), ("Volume", "Volume"), ("Media", "Play"), ("NowPlaying", "Play"),
            ("HomeAssistant", "HomeAssistant"), ("Macro", "Bolt"), ("MultiAction", "Bolt"), ("Conditional", "Bolt"), ("StreamDeck", "Elgato"),
            ("YouTube", "YouTube"), ("Streamlabs", "Streamlabs"), ("Go", "Compass"), ("Page", "Compass")
        };
        if (n.StartsWith("StreamDeck", StringComparison.Ordinal)) return "Elgato";
        foreach (var (token, icon) in rules)
            if (n.Contains(token, StringComparison.Ordinal)) return icon;
        return "Computer";
    }

    public static string ForCategoryKey(string? catKey)
        => catKey != null && CategoryIcons.TryGetValue(catKey, out var k) ? k : "Elgato";

    private sealed class ByKeyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string key = value as string ?? "";
            if (key.Length == 0) key = "Bolt";
            var app = Application.Current;
            if (app != null && app.TryGetResource("Icon" + key, app.ActualThemeVariant, out var res) && res is Geometry g) return g;
            return app != null && app.TryGetResource("IconBolt", app.ActualThemeVariant, out var f) ? f as Geometry : null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
    }
}

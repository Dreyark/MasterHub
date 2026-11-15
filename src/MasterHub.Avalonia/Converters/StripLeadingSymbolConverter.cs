using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MasterHub.Avalonia.Converters;

/// <summary>
/// Usuwa wiodący emoji/symbol (np. "📡 Przełącz stream OBS" -> "Przełącz stream OBS").
/// Nazwy akcji w plikach lokalizacji zawierają emoji, a część widoków pokazuje ikonę akcji osobno -
/// bez tego konwertera emoji był wyświetlany dwa razy obok siebie.
/// </summary>
public sealed class StripLeadingSymbolConverter : IValueConverter
{
    public static readonly StripLeadingSymbolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? Strip(s) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Strip(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            // litera, cyfra lub nawias otwierający = początek właściwego tekstu
            if (char.IsLetterOrDigit(c) || c == '(' || c == '[') break;
            i++;
        }
        // Nic poza symbolami albo brak wiodącego symbolu - zwracamy oryginał bez zmian.
        return i == 0 || i >= text.Length ? text : text[i..];
    }
}

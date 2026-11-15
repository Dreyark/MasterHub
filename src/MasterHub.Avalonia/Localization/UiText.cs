using MasterHub.Avalonia.Converters;

namespace MasterHub.Avalonia.Localization;

/// <summary>
/// Czyszczenie napisów interfejsu z początkowych emoji ("🎬 Uruchom makro" -> "Uruchom makro").
/// Kolorowe emoji nie pasują do ikon wektorowych aplikacji i bywają ucinane; teksty w plikach lokalizacji
/// zostają bez zmian (używa ich też renderer ekranów LCD).
/// </summary>
public static class UiText
{
    public static string Clean(string? text) => string.IsNullOrEmpty(text) ? text ?? "" : StripLeadingSymbolConverter.Strip(text);

    /// <summary>Konwerter dla bindingów XAML (używany przez rozszerzenie Loc).</summary>
    public static readonly global::Avalonia.Data.Converters.IValueConverter Converter = StripLeadingSymbolConverter.Instance;
}

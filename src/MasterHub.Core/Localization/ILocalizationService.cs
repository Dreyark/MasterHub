using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace MasterHub.Core.Localization;

public class LanguageOption
{
    public string Code { get; }
    public string DisplayName { get; }
    public string Flag => Code.ToLowerInvariant() switch
    {
        "pl" or "pl-pl" => "🇵🇱",
        "en" or "en-us" or "en-gb" => "🇬🇧",
        "de" or "de-de" => "🇩🇪",
        "fr" or "fr-fr" => "🇫🇷",
        "es" or "es-es" => "🇪🇸",
        "ja" or "ja-jp" => "🇯🇵",
        _ => "🌐"
    };

    public LanguageOption(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}

public interface ILocalizationService : INotifyPropertyChanged
{
    string CurrentLanguageCode { get; }
    IReadOnlyList<LanguageOption> SupportedLanguages { get; }
    string this[string key] { get; }
    string GetString(string key, string fallback = "");
    void SetLanguage(string languageCode);
    event Action<string>? LanguageChanged;
}

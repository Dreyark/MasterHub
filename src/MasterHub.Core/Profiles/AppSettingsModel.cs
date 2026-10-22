namespace MasterHub.Core.Profiles;

/// <summary>
/// Model ogólnych ustawień aplikacji (autostart, zachowanie okna, język).
/// </summary>
public class AppSettingsModel
{
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = true;
    public string LanguageCode { get; set; } = "pl";
}

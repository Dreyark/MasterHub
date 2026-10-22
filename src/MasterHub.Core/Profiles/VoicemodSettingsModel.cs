namespace MasterHub.Core.Profiles;

/// <summary>
/// Model konfiguracyjny połączenia z Voicemod (interfejs WebSocket Control API).
/// </summary>
public class VoicemodSettingsModel
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 59129;
    public string ApiKey { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
    public int ReconnectIntervalSeconds { get; set; } = 3;
    public bool FallbackToDesktopHotkeys { get; set; } = true;
}

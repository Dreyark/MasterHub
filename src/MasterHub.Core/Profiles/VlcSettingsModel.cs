namespace MasterHub.Core.Profiles;

/// <summary>
/// Model konfiguracyjny połączenia z VLC Media Player (interfejs HTTP / Lua Web).
/// </summary>
public class VlcSettingsModel
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public string Password { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
    public int StatusPollIntervalSeconds { get; set; } = 2;
    public bool FallbackToDesktopHotkeys { get; set; } = true;
}

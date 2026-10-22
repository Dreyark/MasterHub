namespace MasterHub.Core.Profiles;

public class HomeAssistantSettingsModel
{
    public string ServerUrl { get; set; } = "http://homeassistant.local:8123";
    public string AccessToken { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
    public int StatusPollIntervalSeconds { get; set; } = 2;
}

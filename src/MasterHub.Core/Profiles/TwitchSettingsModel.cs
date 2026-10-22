namespace MasterHub.Core.Profiles;

public class TwitchSettingsModel
{
    public string Channel { get; set; } = "";
    public string BotUsername { get; set; } = "";
    public string OAuthToken { get; set; } = "";
    public string ClientId { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
}

namespace MasterHub.Core.Profiles;

public class KickSettingsModel
{
    public string Channel { get; set; } = "";
    public string BotUsername { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
}

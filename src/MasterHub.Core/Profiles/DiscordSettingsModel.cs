namespace MasterHub.Core.Profiles;

public class DiscordSettingsModel
{
    public string ClientId { get; set; } = "207646673902501888";
    public string ClientSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string MuteHotkey { get; set; } = "Ctrl+Shift+M";
    public string DeafenHotkey { get; set; } = "Ctrl+Shift+D";
    public bool UseRpc { get; set; } = false;
}

namespace MasterHub.Core.Profiles;

public class DiscordSettingsModel
{
    public string ClientId { get; set; } = "207646673902501888";
    public string ClientSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string MuteHotkey { get; set; } = "Ctrl+Shift+M";
    public string DeafenHotkey { get; set; } = "Ctrl+Shift+D";
    // Discord RPC nie ma komend kamery ani udostepniania ekranu - sterujemy nimi skrotami (przypisanymi w Discordzie).
    public string CameraHotkey { get; set; } = "Ctrl+Shift+O";
    public string ScreenShareHotkey { get; set; } = "Ctrl+Shift+S";
    public bool UseRpc { get; set; } = false;
}

namespace MasterHub.Core.Profiles;

public class ObsSettingsModel
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4455;
    public string Password { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
}

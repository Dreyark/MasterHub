namespace MasterHub.Core.Profiles;

public class JetKvmSettingsModel
{
    public string Host { get; set; } = "192.168.1.100";
    public int Port { get; set; } = 80;
    public bool UseHttps { get; set; } = false;
    public string LocalPassword { get; set; } = "";

    /// <summary>
    /// Alias dla wstecznej kompatybilności z serializacją i profilem.
    /// W sieci lokalnej JetKVM nie wymaga klucza API, jedynie opcjonalnego hasła urządzenia.
    /// </summary>
    public string AuthToken
    {
        get => LocalPassword;
        set => LocalPassword = value;
    }

    public bool AutoConnect { get; set; } = false;
    public int StatusPollIntervalSeconds { get; set; } = 3;
}


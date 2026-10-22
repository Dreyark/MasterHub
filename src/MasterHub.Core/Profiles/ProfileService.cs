using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MasterHub.Core.Security;

namespace MasterHub.Core.Profiles;

public class ProfileService : IProfileService
{
    private readonly string _profilesDirectory;
    private readonly string _settingsFilePath;
    private readonly ISecretProtectionService _secretProtection;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private string _activeProfileId = "";
    private List<string> _savedColors = new()
    {
        "#00FFCC", "#7928CA", "#38BDF8", "#EF4444", "#F59E0B", "#10B981"
    };
    private DiscordSettingsModel _discordSettings = new();
    private ObsSettingsModel _obsSettings = new();
    private TwitchSettingsModel _twitchSettings = new();
    private KickSettingsModel _kickSettings = new();
    private JetKvmSettingsModel _jetKvmSettings = new();
    private SpotifySettingsModel _spotifySettings = new();
    private VlcSettingsModel _vlcSettings = new();
    private VoicemodSettingsModel _voicemodSettings = new();
    private HomeAssistantSettingsModel _homeAssistantSettings = new();
    private AppSettingsModel _appSettings = new();

    public ProfileService(string? customBasePath = null, ISecretProtectionService? secretProtection = null)
    {
        _secretProtection = secretProtection ?? new SecretProtectionService();
        string baseDir = customBasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasterHub");

        _profilesDirectory = Path.Combine(baseDir, "profiles");
        _settingsFilePath = Path.Combine(baseDir, "settings.json");

        EnsureDirectoryAndDefaultProfile();
    }

    private void EnsureDirectoryAndDefaultProfile()
    {
        try
        {
            if (!Directory.Exists(_profilesDirectory))
            {
                Directory.CreateDirectory(_profilesDirectory);
            }

            LoadSettings();

            var existing = GetAllProfiles();
            if (existing.Count == 0)
            {
                var defaultProf = ProfileModel.CreateEmpty("Domyślny", isDefault: true);
                defaultProf.Id = "default";
                SaveProfile(defaultProf);
                _activeProfileId = defaultProf.Id;
                SaveSettings();
            }
            else
            {
                var def = existing.FirstOrDefault(p => p.IsDefault) ?? existing.FirstOrDefault(p => p.Id == "default") ?? existing[0];
                if (def.Id == "default" && (def.LedColorHex == "#00FFCC" || def.Sliders.Any(s => s.ColorHex == "#00FFCC")))
                {
                    def.LedColorHex = "#000000";
                    def.BaseBrightness = 0;
                    foreach (var s in def.Sliders)
                    {
                        s.ColorHex = "#000000";
                        s.Brightness = 0;
                    }
                    foreach (var r in def.Rollers)
                    {
                        r.ColorHex = "#000000";
                        r.Brightness = 0;
                    }
                    SaveProfile(def);
                }

                if (string.IsNullOrWhiteSpace(_activeProfileId) || existing.All(p => p.Id != _activeProfileId))
                {
                    _activeProfileId = def.Id;
                    SaveSettings();
                }
            }
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ActiveProfileId", out var prop))
                {
                    _activeProfileId = prop.GetString() ?? "";
                }
                if (doc.RootElement.TryGetProperty("SavedColors", out var colProp) && colProp.ValueKind == JsonValueKind.Array)
                {
                    var loaded = colProp.EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    if (loaded.Count > 0)
                    {
                        _savedColors = loaded;
                    }
                }
                if (doc.RootElement.TryGetProperty("DiscordSettings", out var dcProp) && dcProp.ValueKind == JsonValueKind.Object)
                {
                    _discordSettings = JsonSerializer.Deserialize<DiscordSettingsModel>(dcProp.GetRawText(), _jsonOptions) ?? new();
                    _discordSettings.ClientSecret = _secretProtection.Unprotect(_discordSettings.ClientSecret) ?? "";
                    _discordSettings.AccessToken = _secretProtection.Unprotect(_discordSettings.AccessToken) ?? "";
                }
                if (doc.RootElement.TryGetProperty("ObsSettings", out var obsProp) && obsProp.ValueKind == JsonValueKind.Object)
                {
                    _obsSettings = JsonSerializer.Deserialize<ObsSettingsModel>(obsProp.GetRawText(), _jsonOptions) ?? new();
                    _obsSettings.Password = _secretProtection.Unprotect(_obsSettings.Password) ?? "";
                }
                if (doc.RootElement.TryGetProperty("TwitchSettings", out var twitchProp) && twitchProp.ValueKind == JsonValueKind.Object)
                {
                    _twitchSettings = JsonSerializer.Deserialize<TwitchSettingsModel>(twitchProp.GetRawText(), _jsonOptions) ?? new();
                    _twitchSettings.OAuthToken = _secretProtection.Unprotect(_twitchSettings.OAuthToken) ?? "";
                }
                if (doc.RootElement.TryGetProperty("KickSettings", out var kickProp) && kickProp.ValueKind == JsonValueKind.Object)
                {
                    _kickSettings = JsonSerializer.Deserialize<KickSettingsModel>(kickProp.GetRawText(), _jsonOptions) ?? new();
                    _kickSettings.AuthToken = _secretProtection.Unprotect(_kickSettings.AuthToken) ?? "";
                }
                if (doc.RootElement.TryGetProperty("JetKvmSettings", out var jetProp) && jetProp.ValueKind == JsonValueKind.Object)
                {
                    _jetKvmSettings = JsonSerializer.Deserialize<JetKvmSettingsModel>(jetProp.GetRawText(), _jsonOptions) ?? new();
                    _jetKvmSettings.AuthToken = _secretProtection.Unprotect(_jetKvmSettings.AuthToken) ?? "";
                }
                if (doc.RootElement.TryGetProperty("SpotifySettings", out var spotifyProp) && spotifyProp.ValueKind == JsonValueKind.Object)
                {
                    _spotifySettings = JsonSerializer.Deserialize<SpotifySettingsModel>(spotifyProp.GetRawText(), _jsonOptions) ?? new();
                    _spotifySettings.AccessToken = _secretProtection.Unprotect(_spotifySettings.AccessToken) ?? "";
                    _spotifySettings.RefreshToken = _secretProtection.Unprotect(_spotifySettings.RefreshToken) ?? "";
                    _spotifySettings.ClientSecret = _secretProtection.Unprotect(_spotifySettings.ClientSecret) ?? "";
                }
                if (doc.RootElement.TryGetProperty("VlcSettings", out var vlcProp) && vlcProp.ValueKind == JsonValueKind.Object)
                {
                    _vlcSettings = JsonSerializer.Deserialize<VlcSettingsModel>(vlcProp.GetRawText(), _jsonOptions) ?? new();
                    _vlcSettings.Password = _secretProtection.Unprotect(_vlcSettings.Password) ?? "";
                }
                if (doc.RootElement.TryGetProperty("VoicemodSettings", out var vmProp) && vmProp.ValueKind == JsonValueKind.Object)
                {
                    _voicemodSettings = JsonSerializer.Deserialize<VoicemodSettingsModel>(vmProp.GetRawText(), _jsonOptions) ?? new();
                    _voicemodSettings.ApiKey = _secretProtection.Unprotect(_voicemodSettings.ApiKey) ?? "";
                }
                if (doc.RootElement.TryGetProperty("HomeAssistantSettings", out var haProp) && haProp.ValueKind == JsonValueKind.Object)
                {
                    _homeAssistantSettings = JsonSerializer.Deserialize<HomeAssistantSettingsModel>(haProp.GetRawText(), _jsonOptions) ?? new();
                    _homeAssistantSettings.AccessToken = _secretProtection.Unprotect(_homeAssistantSettings.AccessToken) ?? "";
                    _homeAssistantSettings.ServerUrl = _secretProtection.Unprotect(_homeAssistantSettings.ServerUrl) ?? _homeAssistantSettings.ServerUrl;
                }
                if (doc.RootElement.TryGetProperty("AppSettings", out var appProp) && appProp.ValueKind == JsonValueKind.Object)
                {
                    _appSettings = JsonSerializer.Deserialize<AppSettingsModel>(appProp.GetRawText(), _jsonOptions) ?? new();
                }
            }
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            System.Text.Json.Nodes.JsonObject rootObj = new();
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    string existing = File.ReadAllText(_settingsFilePath);
                    if (!string.IsNullOrWhiteSpace(existing) &&
                        System.Text.Json.Nodes.JsonNode.Parse(existing) is System.Text.Json.Nodes.JsonObject parsedObj)
                    {
                        rootObj = parsedObj;
                    }
                }
                catch { }
            }

            rootObj["ActiveProfileId"] = _activeProfileId;
            var colorsArray = new System.Text.Json.Nodes.JsonArray();
            foreach (var c in _savedColors)
            {
                colorsArray.Add(c);
            }
            rootObj["SavedColors"] = colorsArray;

            // Zabezpieczenie haseł i tokenów przed zapisem na dysk
            var dcToSave = new DiscordSettingsModel
            {
                ClientId = _discordSettings.ClientId,
                ClientSecret = _secretProtection.Protect(_discordSettings.ClientSecret) ?? "",
                AccessToken = _secretProtection.Protect(_discordSettings.AccessToken) ?? "",
                MuteHotkey = _discordSettings.MuteHotkey,
                DeafenHotkey = _discordSettings.DeafenHotkey,
                UseRpc = _discordSettings.UseRpc
            };
            var obsToSave = new ObsSettingsModel
            {
                Host = _obsSettings.Host,
                Port = _obsSettings.Port,
                Password = _secretProtection.Protect(_obsSettings.Password) ?? "",
                AutoConnect = _obsSettings.AutoConnect
            };
            var twitchToSave = new TwitchSettingsModel
            {
                Channel = _twitchSettings.Channel,
                BotUsername = _twitchSettings.BotUsername,
                OAuthToken = _secretProtection.Protect(_twitchSettings.OAuthToken) ?? "",
                ClientId = _twitchSettings.ClientId,
                AutoConnect = _twitchSettings.AutoConnect
            };
            var kickToSave = new KickSettingsModel
            {
                Channel = _kickSettings.Channel,
                BotUsername = _kickSettings.BotUsername,
                AuthToken = _secretProtection.Protect(_kickSettings.AuthToken) ?? "",
                AutoConnect = _kickSettings.AutoConnect
            };
            var jetToSave = new JetKvmSettingsModel
            {
                Host = _jetKvmSettings.Host,
                Port = _jetKvmSettings.Port,
                UseHttps = _jetKvmSettings.UseHttps,
                AuthToken = _secretProtection.Protect(_jetKvmSettings.AuthToken) ?? "",
                AutoConnect = _jetKvmSettings.AutoConnect,
                StatusPollIntervalSeconds = _jetKvmSettings.StatusPollIntervalSeconds
            };
            var spotifyToSave = new SpotifySettingsModel
            {
                ClientId = _spotifySettings.ClientId,
                ClientSecret = _secretProtection.Protect(_spotifySettings.ClientSecret) ?? "",
                AccessToken = _secretProtection.Protect(_spotifySettings.AccessToken) ?? "",
                RefreshToken = _secretProtection.Protect(_spotifySettings.RefreshToken) ?? "",
                TokenExpiresAtUtc = _spotifySettings.TokenExpiresAtUtc,
                AutoConnect = _spotifySettings.AutoConnect,
                StatusPollIntervalSeconds = _spotifySettings.StatusPollIntervalSeconds,
                FallbackToDesktopHotkeys = _spotifySettings.FallbackToDesktopHotkeys
            };
            var vlcToSave = new VlcSettingsModel
            {
                Host = _vlcSettings.Host,
                Port = _vlcSettings.Port,
                Password = _secretProtection.Protect(_vlcSettings.Password) ?? "",
                AutoConnect = _vlcSettings.AutoConnect,
                StatusPollIntervalSeconds = _vlcSettings.StatusPollIntervalSeconds,
                FallbackToDesktopHotkeys = _vlcSettings.FallbackToDesktopHotkeys
            };
            var voicemodToSave = new VoicemodSettingsModel
            {
                Host = _voicemodSettings.Host,
                Port = _voicemodSettings.Port,
                ApiKey = _secretProtection.Protect(_voicemodSettings.ApiKey) ?? "",
                AutoConnect = _voicemodSettings.AutoConnect,
                ReconnectIntervalSeconds = _voicemodSettings.ReconnectIntervalSeconds,
                FallbackToDesktopHotkeys = _voicemodSettings.FallbackToDesktopHotkeys
            };
            var haToSave = new HomeAssistantSettingsModel
            {
                ServerUrl = _secretProtection.Protect(_homeAssistantSettings.ServerUrl) ?? _homeAssistantSettings.ServerUrl,
                AccessToken = _secretProtection.Protect(_homeAssistantSettings.AccessToken) ?? "",
                AutoConnect = _homeAssistantSettings.AutoConnect,
                StatusPollIntervalSeconds = _homeAssistantSettings.StatusPollIntervalSeconds
            };

            rootObj["DiscordSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(dcToSave, _jsonOptions));
            rootObj["ObsSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(obsToSave, _jsonOptions));
            rootObj["TwitchSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(twitchToSave, _jsonOptions));
            rootObj["KickSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(kickToSave, _jsonOptions));
            rootObj["JetKvmSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(jetToSave, _jsonOptions));
            rootObj["SpotifySettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(spotifyToSave, _jsonOptions));
            rootObj["VlcSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(vlcToSave, _jsonOptions));
            rootObj["VoicemodSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(voicemodToSave, _jsonOptions));
            rootObj["HomeAssistantSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(haToSave, _jsonOptions));
            rootObj["AppSettings"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(_appSettings, _jsonOptions));

            string json = rootObj.ToJsonString(_jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch { }
    }

    public DiscordSettingsModel GetDiscordSettings()
    {
        return _discordSettings;
    }

    public void SaveDiscordSettings(DiscordSettingsModel settings)
    {
        _discordSettings = settings ?? new();
        SaveSettings();
    }

    public ObsSettingsModel GetObsSettings()
    {
        return _obsSettings;
    }

    public void SaveObsSettings(ObsSettingsModel settings)
    {
        _obsSettings = settings ?? new();
        SaveSettings();
    }

    public TwitchSettingsModel GetTwitchSettings()
    {
        return _twitchSettings;
    }

    public void SaveTwitchSettings(TwitchSettingsModel settings)
    {
        _twitchSettings = settings ?? new();
        SaveSettings();
    }

    public KickSettingsModel GetKickSettings()
    {
        return _kickSettings;
    }

    public void SaveKickSettings(KickSettingsModel settings)
    {
        _kickSettings = settings ?? new();
        SaveSettings();
    }

    public JetKvmSettingsModel GetJetKvmSettings()
    {
        return _jetKvmSettings;
    }

    public void SaveJetKvmSettings(JetKvmSettingsModel settings)
    {
        _jetKvmSettings = settings ?? new();
        SaveSettings();
    }

    public SpotifySettingsModel GetSpotifySettings()
    {
        return _spotifySettings;
    }

    public void SaveSpotifySettings(SpotifySettingsModel settings)
    {
        _spotifySettings = settings ?? new();
        SaveSettings();
    }

    public VlcSettingsModel GetVlcSettings()
    {
        return _vlcSettings;
    }

    public void SaveVlcSettings(VlcSettingsModel settings)
    {
        _vlcSettings = settings ?? new();
        SaveSettings();
    }

    public VoicemodSettingsModel GetVoicemodSettings()
    {
        return _voicemodSettings;
    }

    public void SaveVoicemodSettings(VoicemodSettingsModel settings)
    {
        _voicemodSettings = settings ?? new();
        SaveSettings();
    }

    public List<string> GetSavedColors()
    {
        return new List<string>(_savedColors);
    }

    public void SaveCustomColors(IEnumerable<string> colors)
    {
        _savedColors = colors.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        SaveSettings();
    }

    public List<ProfileModel> GetAllProfiles()
    {
        var list = new List<ProfileModel>();
        if (!Directory.Exists(_profilesDirectory)) return list;

        var files = Directory.GetFiles(_profilesDirectory, "*.json");
        foreach (var file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                var prof = JsonSerializer.Deserialize<ProfileModel>(json, _jsonOptions);
                if (prof != null)
                {
                    prof.ObsPassword = _secretProtection.Unprotect(prof.ObsPassword);
                    prof.TwitchOAuthToken = _secretProtection.Unprotect(prof.TwitchOAuthToken);
                    prof.YouTubeApiKey = _secretProtection.Unprotect(prof.YouTubeApiKey);
                    prof.StreamlabsApiToken = _secretProtection.Unprotect(prof.StreamlabsApiToken);
                    prof.DiscordAccessToken = _secretProtection.Unprotect(prof.DiscordAccessToken);
                    list.Add(prof);
                }
            }
            catch { }
        }

        return list.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToList();
    }

    public ProfileModel GetActiveProfile()
    {
        var all = GetAllProfiles();
        var current = all.FirstOrDefault(p => p.Id == _activeProfileId)
                      ?? all.FirstOrDefault(p => p.IsDefault)
                      ?? all.FirstOrDefault();

        if (current == null)
        {
            current = ProfileModel.CreateEmpty("Domyślny", isDefault: true);
            current.Id = "default";
            SaveProfile(current);
            _activeProfileId = current.Id;
            SaveSettings();
        }

        return current;
    }

    public ProfileModel CreateProfile(string name, bool setAsActive = true)
    {
        string cleanName = string.IsNullOrWhiteSpace(name) ? "Nowy profil" : name.Trim();
        var prof = ProfileModel.CreateEmpty(cleanName, isDefault: false);
        SaveProfile(prof);

        if (setAsActive)
        {
            SetActiveProfile(prof.Id);
        }

        return prof;
    }

    public void SaveProfile(ProfileModel profile)
    {
        if (profile == null) return;
        string path = Path.Combine(_profilesDirectory, $"{profile.Id}.json");

        try
        {
            // Szyfrowanie poświadczeń na dysku z zachowaniem czystego stanu w pamięci podręcznej RAM
            string rawJson = JsonSerializer.Serialize(profile, _jsonOptions);
            if (System.Text.Json.Nodes.JsonNode.Parse(rawJson) is System.Text.Json.Nodes.JsonObject node)
            {
                if (node.ContainsKey("ObsPassword") && node["ObsPassword"] != null)
                {
                    string? plain = node["ObsPassword"]?.GetValue<string>();
                    node["ObsPassword"] = _secretProtection.Protect(plain);
                }
                if (node.ContainsKey("TwitchOAuthToken") && node["TwitchOAuthToken"] != null)
                {
                    string? plain = node["TwitchOAuthToken"]?.GetValue<string>();
                    node["TwitchOAuthToken"] = _secretProtection.Protect(plain);
                }
                if (node.ContainsKey("YouTubeApiKey") && node["YouTubeApiKey"] != null)
                {
                    string? plain = node["YouTubeApiKey"]?.GetValue<string>();
                    node["YouTubeApiKey"] = _secretProtection.Protect(plain);
                }
                if (node.ContainsKey("StreamlabsApiToken") && node["StreamlabsApiToken"] != null)
                {
                    string? plain = node["StreamlabsApiToken"]?.GetValue<string>();
                    node["StreamlabsApiToken"] = _secretProtection.Protect(plain);
                }
                if (node.ContainsKey("DiscordAccessToken") && node["DiscordAccessToken"] != null)
                {
                    string? plain = node["DiscordAccessToken"]?.GetValue<string>();
                    node["DiscordAccessToken"] = _secretProtection.Protect(plain);
                }

                File.WriteAllText(path, node.ToJsonString(_jsonOptions));
                return;
            }
        }
        catch { }

        string fallbackJson = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(path, fallbackJson);
    }

    public void DeleteProfile(string profileId)
    {
        string path = Path.Combine(_profilesDirectory, $"{profileId}.json");
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }

        var remaining = GetAllProfiles();
        if (remaining.Count == 0)
        {
            var def = ProfileModel.CreateEmpty("Domyślny", isDefault: true);
            def.Id = "default";
            SaveProfile(def);
            _activeProfileId = def.Id;
            SaveSettings();
        }
        else if (_activeProfileId == profileId)
        {
            var fallback = remaining.FirstOrDefault(p => p.IsDefault) ?? remaining[0];
            _activeProfileId = fallback.Id;
            SaveSettings();
        }
    }

    public void SetActiveProfile(string profileId)
    {
        _activeProfileId = profileId;
        SaveSettings();
    }

    public void SetDefaultProfile(string profileId)
    {
        var all = GetAllProfiles();
        foreach (var p in all)
        {
            bool wasDefault = p.IsDefault;
            p.IsDefault = (p.Id == profileId);
            if (p.IsDefault != wasDefault)
            {
                SaveProfile(p);
            }
        }
    }

    // --- ETAP 2: EKSPORT I IMPORT PROFILI ---

    public string ExportProfileToJson(string profileId)
    {
        var all = GetAllProfiles();
        var prof = all.FirstOrDefault(p => p.Id == profileId) ?? GetActiveProfile();

        // Klon profilu do celów eksportu, aby nie modyfikować obiektu w pamięci
        string clonedJson = JsonSerializer.Serialize(prof, _jsonOptions);
        var exportModel = JsonSerializer.Deserialize<ProfileModel>(clonedJson, _jsonOptions) ?? prof;

        // RYGORYSTYCZNA SANITYZACJA: Całkowite wyczyszczenie haseł i tokenów aplikacji trzecich
        exportModel.ObsPassword = null;
        exportModel.TwitchOAuthToken = null;
        exportModel.YouTubeApiKey = null;
        exportModel.StreamlabsApiToken = null;
        exportModel.DiscordAccessToken = null;

        return JsonSerializer.Serialize(exportModel, _jsonOptions);
    }

    public void ExportProfileToFile(string profileId, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        string json = ExportProfileToJson(profileId);
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(filePath, json);
    }

    public ProfileModel ImportProfileFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Zawartość JSON profilu jest pusta.", nameof(json));

        var imported = JsonSerializer.Deserialize<ProfileModel>(json, _jsonOptions)
                       ?? throw new InvalidOperationException("Nie udało się odczytać profilu z formatu JSON.");

        // Ochrona bezpieczeństwa: importowany profil nie może nadpisać ani wstrzyknąć obcych poświadczeń
        imported.ObsPassword = null;
        imported.TwitchOAuthToken = null;
        imported.YouTubeApiKey = null;
        imported.StreamlabsApiToken = null;
        imported.DiscordAccessToken = null;

        var existing = GetAllProfiles();
        bool idCollision = existing.Any(p => p.Id == imported.Id);
        bool nameCollision = existing.Any(p => string.Equals(p.Name, imported.Name, StringComparison.OrdinalIgnoreCase));

        if (idCollision || string.IsNullOrWhiteSpace(imported.Id))
        {
            // Generuj nowe ID, by nie nadpisać istniejącego profilu
            imported.Id = Guid.NewGuid().ToString("N");
        }

        if (nameCollision || idCollision || imported.IsDefault)
        {
            imported.Name = $"{imported.Name} (Import)";
            imported.IsDefault = false;
        }

        // Upewnij się, że struktura zawiera 15 klawiszy i poprawne strony
        if (imported.Keys.Count < 15)
        {
            var fallback = ProfileModel.CreateEmpty(imported.Name);
            for (int i = imported.Keys.Count; i < 15; i++)
            {
                imported.Keys.Add(fallback.Keys[i]);
            }
        }

        if (imported.Pages.Count == 0)
        {
            imported.Pages.Add(new KeyPageModel
            {
                PageIndex = 0,
                Name = "Strona 1",
                Keys = new List<KeyAssignmentModel>(imported.Keys)
            });
        }

        SaveProfile(imported);
        return imported;
    }

    public ProfileModel ImportProfileFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException($"Plik profilu '{filePath}' nie został odnaleziony.");

        if (filePath.EndsWith(".mhz", StringComparison.OrdinalIgnoreCase))
        {
            var importedList = CoolerMasterMhzImporter.Import(filePath);
            if (importedList.Count == 0)
                throw new InvalidOperationException("Plik .mhz nie zawiera żadnego poprawnego profilu MasterHUB.");

            var existing = GetAllProfiles();
            foreach (var prof in importedList)
            {
                if (existing.Any(p => p.Id == prof.Id))
                {
                    prof.Id = Guid.NewGuid().ToString("N");
                }
                SaveProfile(prof);
            }
            return importedList[0];
        }

        string json = File.ReadAllText(filePath);
        return ImportProfileFromJson(json);
    }

    public HomeAssistantSettingsModel GetHomeAssistantSettings() => _homeAssistantSettings;

    public void SaveHomeAssistantSettings(HomeAssistantSettingsModel settings)
    {
        _homeAssistantSettings = settings ?? new();
        SaveSettings();
    }

    public AppSettingsModel GetAppSettings() => _appSettings;

    public void SaveAppSettings(AppSettingsModel settings)
    {
        _appSettings = settings ?? new();
        SaveSettings();
    }
}

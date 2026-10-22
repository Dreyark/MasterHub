using System.Collections.Generic;

namespace MasterHub.Core.Profiles;

public interface IProfileService
{
    List<ProfileModel> GetAllProfiles();
    ProfileModel GetActiveProfile();
    ProfileModel CreateProfile(string name, bool setAsActive = true);
    void SaveProfile(ProfileModel profile);
    void DeleteProfile(string profileId);
    void SetActiveProfile(string profileId);
    void SetDefaultProfile(string profileId);
    List<string> GetSavedColors();
    void SaveCustomColors(IEnumerable<string> colors);

    // --- ETAP 2: EKSPORT I IMPORT PROFILI ---
    string ExportProfileToJson(string profileId);
    void ExportProfileToFile(string profileId, string filePath);
    ProfileModel ImportProfileFromJson(string json);
    ProfileModel ImportProfileFromFile(string filePath);

    // --- INTEGRACJA DISCORD ---
    DiscordSettingsModel GetDiscordSettings();
    void SaveDiscordSettings(DiscordSettingsModel settings);

    // --- INTEGRACJA OBS STUDIO ---
    ObsSettingsModel GetObsSettings();
    void SaveObsSettings(ObsSettingsModel settings);

    // --- INTEGRACJA TWITCH ---
    TwitchSettingsModel GetTwitchSettings();
    void SaveTwitchSettings(TwitchSettingsModel settings);

    // --- INTEGRACJA KICK ---
    KickSettingsModel GetKickSettings();
    void SaveKickSettings(KickSettingsModel settings);

    // --- INTEGRACJA JETKVM ---
    JetKvmSettingsModel GetJetKvmSettings();
    void SaveJetKvmSettings(JetKvmSettingsModel settings);

    // --- INTEGRACJA SPOTIFY ---
    SpotifySettingsModel GetSpotifySettings();
    void SaveSpotifySettings(SpotifySettingsModel settings);

    // --- INTEGRACJA VLC MEDIA PLAYER ---
    VlcSettingsModel GetVlcSettings();
    void SaveVlcSettings(VlcSettingsModel settings);

    // --- INTEGRACJA VOICEMOD ---
    VoicemodSettingsModel GetVoicemodSettings();
    void SaveVoicemodSettings(VoicemodSettingsModel settings);

    // --- INTEGRACJA HOME ASSISTANT ---
    HomeAssistantSettingsModel GetHomeAssistantSettings();
    void SaveHomeAssistantSettings(HomeAssistantSettingsModel settings);

    // --- USTAWIENIA APLIKACJI I SYSTEMOWE ---
    AppSettingsModel GetAppSettings();
    void SaveAppSettings(AppSettingsModel settings);
}

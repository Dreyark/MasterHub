using System.Collections.Generic;

namespace MasterHub.Core.Profiles;

public interface IProfileService
{
    List<ProfileModel> GetAllProfiles();
    ProfileModel GetActiveProfile();
    ProfileModel CreateProfile(string name, bool setAsActive = true);
    void SaveProfile(ProfileModel profile);
    void DeleteProfile(string profileId);
    /// <param name="persist">false = przełączenie automatyczne (Smart Profiles): nie zapamiętuj w settings.json, aby po restarcie wrócić do ostatnio wybranego ręcznie profilu.</param>
    void SetActiveProfile(string profileId, bool persist = true);
    void SetDefaultProfile(string profileId);
    List<string> GetSavedColors();
    void SaveCustomColors(IEnumerable<string> colors);

    // --- KOPIE ZAPASOWE I KWARANTANNA (T-02) ---
    Backup.IBackupService Backups { get; }
    /// <summary>Pliki, których nie dało się odczytać – przeniesione do <c>*.corrupt-&lt;czas&gt;</c> (nigdy nie usuwane).</summary>
    IReadOnlyList<string> QuarantinedFiles { get; }
    bool RestoreBackup(string name);

    // --- WERSJA ROBOCZA / PODGLĄD (T-08) ---
    /// <summary>Raised when a draft started, was kept, thrown away or put back.</summary>
    event System.Action? DraftChanged;
    /// <summary>The draft that is on the deck now (not saved among the profiles), or <c>null</c>.</summary>
    Draft.DraftInfo? ActiveDraft { get; }
    /// <summary>A draft the last run left behind (crash); offered at start.</summary>
    Draft.DraftInfo? PendingDraft { get; }
    ProfileModel BeginDraft(ProfileModel draft, Draft.DraftMode mode, int trialSeconds = 0);
    ProfileModel KeepDraft(string? name = null);
    void DiscardDraft();
    ProfileModel? ResumePendingDraft();
    void DropPendingDraft();

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

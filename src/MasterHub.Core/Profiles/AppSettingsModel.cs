namespace MasterHub.Core.Profiles;

/// <summary>
/// Model ogólnych ustawień aplikacji (autostart, zachowanie okna, język).
/// </summary>
public class AppSettingsModel
{
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = true;
    public string LanguageCode { get; set; } = "pl";
    /// <summary>Boczny pasek nawigacji zwinięty do samych ikon (brak pola w starszym JSON = rozwinięty).</summary>
    public bool SidebarCollapsed { get; set; } = false;
    /// <summary>Globalny przełącznik Smart Profiles (automatyczne przełączanie profili wg aplikacji na pierwszym planie).</summary>
    public bool AutoSwitchProfiles { get; set; } = true;
    /// <summary>Czas przytrzymania (ms) rozpoznawany jako gest Hold klawisza.</summary>
    public int HoldDurationMs { get; set; } = 500;
    /// <summary>Maksymalny odstęp (ms) między dotknięciami podwójnego kliknięcia.</summary>
    public int DoublePressIntervalMs { get; set; } = 300;
    /// <summary>Optional folder (e.g. inside OneDrive/Dropbox) that every backup snapshot is also copied to. Empty = off.</summary>
    public string BackupMirrorPath { get; set; } = "";
    /// <summary>Reakcje diody krawędziowej podstawy na sytuacje (mikrofon wyciszony, OBS na żywo…). Domyślnie wyłączone.</summary>
    public Devices.StatusLightSettings StatusLights { get; set; } = new();
    /// <summary>Whether the setup tour (T-08) was offered / finished / dismissed. It is offered once, on a deck that has nothing set up yet.</summary>
    public Draft.TourState TourState { get; set; } = Draft.TourState.NotOffered;
    /// <summary>Prepared profiles and suggestions (T-37). Off until the user says yes; one master switch.</summary>
    public Auto.AssistSettings Assist { get; set; } = new();
    /// <summary>The update check (T-32). Off until the user turns it on and enters a repository.</summary>
    public Updates.UpdateSettings Updates { get; set; } = new();
    /// <summary>T-17 g: a fader on the main volume also shows the Windows volume popup. Off by default.</summary>
    public bool VolumeOsdForFaders { get; set; } = false;
}

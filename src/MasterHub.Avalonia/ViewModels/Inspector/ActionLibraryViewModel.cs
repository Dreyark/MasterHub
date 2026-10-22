using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Localization;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels.Inspector;

public enum ManipulatorType
{
    Key,
    Slider,
    Roller,
    Knob,
    Dial
}

/// <summary>
/// Pozycja w katalogu biblioteki akcji, uniwersalna dla wybranego typu manipulatora sprzętowego.
/// </summary>
public record ActionCatalogItem(
    string Title,
    string Category,
    string Icon,
    ManipulatorType TargetManipulator,
    DeckActionType KeyAction = DeckActionType.None,
    RollerActionType RollerAction = RollerActionType.None,
    AudioTargetType SliderTargetType = AudioTargetType.None,
    string SliderProcessName = "",
    string Description = "",
    string? StreamDeckPluginUuid = null,
    string? StreamDeckActionUuid = null,
    string? IconImagePath = null,
    Bitmap? IconBitmap = null,
    bool ShowTitle = true);

public partial class CategoryFilterItem : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    public CategoryFilterItem(string name, bool isSelected = false)
    {
        Name = name;
        _isSelected = isSelected;
    }
}

/// <summary>
/// ViewModel biblioteki akcji dynamicznie dostosowujący katalog do aktualnie zaznaczonego manipulatora (Klawisz LCD, Suwak, Rolka, Podstawka).
/// </summary>
public partial class ActionLibraryViewModel : ObservableObject
{
    private readonly IObsService? _obsService;
    private readonly IDiscordService? _discordService;
    private readonly ITeamsService? _teamsService;
    private readonly ISystemMonitorService? _systemMonitorService;
    private readonly INowPlayingService? _nowPlayingService;
    private readonly IAudioService? _audioService;
    private readonly IStreamDeckPluginHost? _streamDeckHost;
    private readonly IAppIconExtractorService? _iconExtractor;

    private readonly ObservableCollection<ActionCatalogItem> _allItems = new();

    public ObservableCollection<CategoryFilterItem> Categories { get; } = new();

    [ObservableProperty]
    private string _selectedCategory = "Wszystkie";

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private ActionCatalogItem? _selectedAction;

    [ObservableProperty]
    private ManipulatorType _currentManipulator = ManipulatorType.Key;

    [ObservableProperty]
    private string _contextTitle = "Akcje dla przycisku LCD";

    [ObservableProperty]
    private string _manipulatorBadge = "⌨️ Klawisz LCD";

    public Action<ActionCatalogItem>? ActionSelected { get; set; }

    partial void OnSelectedActionChanged(ActionCatalogItem? value)
    {
        if (value != null)
        {
            ActionSelected?.Invoke(value);
            _selectedAction = null;
            OnPropertyChanged(nameof(SelectedAction));
        }
    }

    public ObservableCollection<ActionCatalogItem> FilteredItems { get; } = new();

    public ActionLibraryViewModel(
        IObsService? obsService = null,
        IDiscordService? discordService = null,
        ITeamsService? teamsService = null,
        ISystemMonitorService? systemMonitorService = null,
        INowPlayingService? nowPlayingService = null,
        IAudioService? audioService = null,
        IStreamDeckPluginHost? streamDeckHost = null,
        IAppIconExtractorService? iconExtractor = null)
    {
        _obsService = obsService;
        _discordService = discordService;
        _teamsService = teamsService;
        _systemMonitorService = systemMonitorService;
        _nowPlayingService = nowPlayingService;
        _audioService = audioService;
        _streamDeckHost = streamDeckHost;
        _iconExtractor = iconExtractor;

        if (_streamDeckHost != null)
        {
            _streamDeckHost.PluginInstalled += _ => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => SetManipulator(CurrentManipulator));
        }

        LocalizationService.Instance.LanguageChanged += _ => SetManipulator(CurrentManipulator);
        SetManipulator(ManipulatorType.Key);
    }

    public void SetManipulator(ManipulatorType type)
    {
        CurrentManipulator = type;
        SearchQuery = "";
        string allCat = LocalizationService.Instance.GetString("Cat_All", "Wszystkie");
        SelectedCategory = allCat;

        Categories.Clear();
        _allItems.Clear();

        switch (type)
        {
            case ManipulatorType.Key:
                ContextTitle = LocalizationService.Instance.GetString("Key_ActionType", "Akcje dla przycisku LCD");
                ManipulatorBadge = "⌨️ " + LocalizationService.Instance.GetString("Deck_ViewKeys", "Klawisze LCD");
                LoadKeyCatalog();
                break;

            case ManipulatorType.Slider:
                ContextTitle = LocalizationService.Instance.GetString("Fader_Target", "Przypisz tor audio dla suwaka");
                ManipulatorBadge = "🎚️ " + LocalizationService.Instance.GetString("Deck_ViewFaders", "Suwak Audio");
                LoadSliderCatalog();
                break;

            case ManipulatorType.Roller:
                ContextTitle = LocalizationService.Instance.GetString("Roller_Action", "Akcje dla pokrętła / rolki");
                ManipulatorBadge = "🎛️ " + LocalizationService.Instance.GetString("Deck_ViewRollers", "Rolka / Pokrętło");
                LoadRollerCatalog();
                break;

            case ManipulatorType.Knob:
                ContextTitle = LocalizationService.Instance.GetString("Knob_Action", "Akcje dla pokrętła enkodera");
                ManipulatorBadge = "🔘 " + LocalizationService.Instance.GetString("Deck_ViewKnobs", "3 Pokrętła");
                LoadRollerCatalog();
                break;

            case ManipulatorType.Dial:
                ContextTitle = LocalizationService.Instance.GetString("Dial_Action", "Akcje dla pokrętła Dial IPS");
                ManipulatorBadge = "🎯 " + LocalizationService.Instance.GetString("Deck_ViewDial", "Pokrętło Dial IPS");
                LoadRollerCatalog();
                break;
        }

        var preferredOrder = new[]
        {
            "Cat_System", "Cat_Audio", "Cat_Media", "Cat_Spotify", "Cat_Vlc", "Cat_Voicemod", "Cat_Monitor", "Cat_OBS",
            "Cat_Twitch", "Cat_Kick", "Cat_JetKvm", "Cat_Streaming", "Cat_Discord",
            "Cat_Teams", "Cat_Razer", "Cat_Wooting", "Cat_Nav", "Cat_MainAudio", "Cat_Applications"
        };
        var preferredCategoryNames = preferredOrder
            .Select(c => LocalizationService.Instance.GetString(c))
            .ToList();

        // 1. Zawsze "Wszystkie" (Cat_All) na pierwszej pozycji (zaznaczona domyślnie)
        Categories.Add(new CategoryFilterItem(allCat, isSelected: true));

        // 2. Dynamiczne dodanie każdej unikalnej kategorii istniejącej w _allItems
        var distinctCategories = _allItems
            .Select(x => x.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c) && !c.Equals(allCat, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c =>
            {
                int idx = preferredCategoryNames.FindIndex(p => p.Equals(c, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? idx : 999;
            })
            .ToList();

        foreach (var cat in distinctCategories)
        {
            Categories.Add(new CategoryFilterItem(cat, isSelected: false));
        }

        ApplyFilter();
    }

    private void LoadKeyCatalog()
    {
        void Add(DeckActionType act, string catKey, string icon)
        {
            _allItems.Add(new(LocalizationService.Instance.GetString($"Action_{act}"), LocalizationService.Instance.GetString(catKey), icon, ManipulatorType.Key, KeyAction: act));
        }

        // System
        Add(DeckActionType.None, "Cat_System", "⚪");
        Add(DeckActionType.DefaultNumber, "Cat_System", "🔢");
        Add(DeckActionType.OpenApplication, "Cat_System", "🚀");
        Add(DeckActionType.OpenWebsite, "Cat_System", "🌐");
        Add(DeckActionType.OpenFile, "Cat_System", "📂");
        Add(DeckActionType.CloseApplication, "Cat_System", "❌");
        Add(DeckActionType.TextPaste, "Cat_System", "📋");
        Add(DeckActionType.Hotkey, "Cat_System", "⌨️");
        Add(DeckActionType.HotkeySwitch, "Cat_System", "🔁");
        Add(DeckActionType.MultiAction, "Cat_System", "⚡");
        Add(DeckActionType.Delay, "Cat_System", "⏳");
        Add(DeckActionType.Screenshot, "Cat_System", "📸");
        Add(DeckActionType.TaskManager, "Cat_System", "📊");
        Add(DeckActionType.Calculator, "Cat_System", "🧮");
        Add(DeckActionType.Browser, "Cat_System", "🌐");
        Add(DeckActionType.SystemSleep, "Cat_System", "💤");
        Add(DeckActionType.SystemLockScreen, "Cat_System", "🔒");
        Add(DeckActionType.SystemShutdown, "Cat_System", "⏻");
        Add(DeckActionType.SystemRestart, "Cat_System", "🔄");
        Add(DeckActionType.SystemScreensaver, "Cat_System", "🖥️");
        Add(DeckActionType.SystemVirtualDesktopNext, "Cat_System", "🗔");
        Add(DeckActionType.SystemVirtualDesktopPrev, "Cat_System", "🖥️");
        Add(DeckActionType.SystemVirtualDesktopCreate, "Cat_System", "➕");
        Add(DeckActionType.SystemVirtualDesktopClose, "Cat_System", "✖️");
        Add(DeckActionType.SystemTaskView, "Cat_System", "🗂️");
        Add(DeckActionType.SystemShowDesktop, "Cat_System", "🖥️");
        Add(DeckActionType.SystemClipboardHistory, "Cat_System", "📋");
        Add(DeckActionType.ConditionalAction, "Cat_System", "🔀");

        // Audio
        Add(DeckActionType.VolumeUp, "Cat_Audio", "🔊");
        Add(DeckActionType.VolumeDown, "Cat_Audio", "🔉");
        Add(DeckActionType.MasterMute, "Cat_Audio", "🔇");
        Add(DeckActionType.MicMute, "Cat_Audio", "🎙️");

        // Media
        Add(DeckActionType.MediaPlayPause, "Cat_Media", "⏯️");
        Add(DeckActionType.MediaNext, "Cat_Media", "⏭️");
        Add(DeckActionType.MediaPrevious, "Cat_Media", "⏮️");
        Add(DeckActionType.MediaStop, "Cat_Media", "⏹️");
        Add(DeckActionType.NowPlaying, "Cat_Media", "🎵");

        // Monitor
        Add(DeckActionType.DisplayCpuUsage, "Cat_Monitor", "📊");
        Add(DeckActionType.DisplayRamUsage, "Cat_Monitor", "🧠");
        Add(DeckActionType.DisplayGpuUsage, "Cat_Monitor", "🎮");
        Add(DeckActionType.DisplayCpuTemp, "Cat_Monitor", "🌡️");
        Add(DeckActionType.DisplayGpuTemp, "Cat_Monitor", "🌡️");
        Add(DeckActionType.DisplayNetworkSpeed, "Cat_Monitor", "📡");
        Add(DeckActionType.DisplayDiskUsage, "Cat_Monitor", "💾");
        Add(DeckActionType.DisplayClock, "Cat_Monitor", "🕐");
        Add(DeckActionType.DisplayDate, "Cat_Monitor", "📅");
        Add(DeckActionType.DisplayFps, "Cat_Monitor", "🎯");

        // OBS
        Add(DeckActionType.ObsToggleStream, "Cat_OBS", "📡");
        Add(DeckActionType.ObsStartStream, "Cat_OBS", "▶️");
        Add(DeckActionType.ObsStopStream, "Cat_OBS", "⏹️");
        Add(DeckActionType.ObsToggleRecord, "Cat_OBS", "🔴");
        Add(DeckActionType.ObsStartRecord, "Cat_OBS", "▶️");
        Add(DeckActionType.ObsStopRecord, "Cat_OBS", "⏹️");
        Add(DeckActionType.ObsToggleVirtualCam, "Cat_OBS", "📹");
        Add(DeckActionType.ObsSaveReplayBuffer, "Cat_OBS", "💾");
        Add(DeckActionType.ObsSceneSwitch, "Cat_OBS", "🎬");
        Add(DeckActionType.ObsStudioModeToggle, "Cat_OBS", "🎭");
        Add(DeckActionType.ObsTriggerTransition, "Cat_OBS", "🔀");

        // Twitch
        Add(DeckActionType.TwitchChatEmoteOnly, "Cat_Twitch", "😀");
        Add(DeckActionType.TwitchChatSlowMode, "Cat_Twitch", "🐢");
        Add(DeckActionType.TwitchChatSubOnly, "Cat_Twitch", "⭐");
        Add(DeckActionType.TwitchClearChat, "Cat_Twitch", "🧹");
        Add(DeckActionType.TwitchSendMessage, "Cat_Twitch", "💬");
        Add(DeckActionType.TwitchCreateClip, "Cat_Twitch", "🎬");
        Add(DeckActionType.TwitchCreateMarker, "Cat_Twitch", "📍");
        Add(DeckActionType.TwitchStartRaid, "Cat_Twitch", "🚀");
        Add(DeckActionType.TwitchCancelRaid, "Cat_Twitch", "🛑");
        Add(DeckActionType.TwitchRunCommercial, "Cat_Twitch", "📺");

        // Kick
        Add(DeckActionType.KickChatEmoteOnly, "Cat_Kick", "😀");
        Add(DeckActionType.KickChatSlowMode, "Cat_Kick", "🐢");
        Add(DeckActionType.KickChatSubOnly, "Cat_Kick", "⭐");
        Add(DeckActionType.KickClearChat, "Cat_Kick", "🧹");
        Add(DeckActionType.KickSendMessage, "Cat_Kick", "💬");
        Add(DeckActionType.KickCreateClip, "Cat_Kick", "🎬");

        // Spotify
        Add(DeckActionType.SpotifyToggleShuffle, "Cat_Spotify", "🔀");
        Add(DeckActionType.SpotifyToggleRepeat, "Cat_Spotify", "🔁");
        Add(DeckActionType.SpotifyLikeTrack, "Cat_Spotify", "💚");
        Add(DeckActionType.SpotifyAddToQueue, "Cat_Spotify", "➕");
        Add(DeckActionType.SpotifyPlayUri, "Cat_Spotify", "▶️");

        // VLC Media Player
        Add(DeckActionType.VlcPlayPause, "Cat_Vlc", "⏯️");
        Add(DeckActionType.VlcStop, "Cat_Vlc", "⏹️");
        Add(DeckActionType.VlcNext, "Cat_Vlc", "⏭️");
        Add(DeckActionType.VlcPrevious, "Cat_Vlc", "⏮️");
        Add(DeckActionType.VlcFullscreenToggle, "Cat_Vlc", "⛶");

        // Voicemod
        Add(DeckActionType.VoicemodVoiceSelect, "Cat_Voicemod", "🤖");
        Add(DeckActionType.VoicemodVoiceToggle, "Cat_Voicemod", "🎙️");
        Add(DeckActionType.VoicemodMuteVoice, "Cat_Voicemod", "🔇");
        Add(DeckActionType.VoicemodHearMyVoiceToggle, "Cat_Voicemod", "🎧");
        Add(DeckActionType.VoicemodBackgroundToggle, "Cat_Voicemod", "🎧");
        Add(DeckActionType.VoicemodPlaySound, "Cat_Voicemod", "🔊");
        Add(DeckActionType.VoicemodStopAllSounds, "Cat_Voicemod", "⏹️");

        // JetKVM
        Add(DeckActionType.JetKvmPowerShort, "Cat_JetKvm", "🔌");
        Add(DeckActionType.JetKvmPowerLong, "Cat_JetKvm", "⏻");
        Add(DeckActionType.JetKvmReset, "Cat_JetKvm", "🔄");
        Add(DeckActionType.JetKvmPowerStatus, "Cat_JetKvm", "💡");

        // Streaming
        Add(DeckActionType.YouTubeLiveViewers, "Cat_Streaming", "▶️");
        Add(DeckActionType.StreamlabsSceneSwitch, "Cat_Streaming", "🎥");

        // Discord
        Add(DeckActionType.DiscordMuteToggle, "Cat_Discord", "🎙️");
        Add(DeckActionType.DiscordDeafenToggle, "Cat_Discord", "🎧");
        Add(DeckActionType.DiscordCameraToggle, "Cat_Discord", "📷");
        Add(DeckActionType.DiscordScreenShareToggle, "Cat_Discord", "🖥️");
        Add(DeckActionType.DiscordPushToTalk, "Cat_Discord", "🗣️");
        Add(DeckActionType.DiscordJoinVoiceChannel, "Cat_Discord", "🔊");

        // Teams
        Add(DeckActionType.TeamsMuteToggle, "Cat_Teams", "🎤");
        Add(DeckActionType.TeamsCameraToggle, "Cat_Teams", "📹");
        Add(DeckActionType.TeamsRaiseHand, "Cat_Teams", "✋");
        Add(DeckActionType.TeamsLeaveMeeting, "Cat_Teams", "📞");

        // Razer
        Add(DeckActionType.RazerDpiCycle, "Cat_Razer", "🎯");
        Add(DeckActionType.RazerDpiUp, "Cat_Razer", "▲");
        Add(DeckActionType.RazerDpiDown, "Cat_Razer", "▼");
        Add(DeckActionType.RazerDpi400, "Cat_Razer", "🎯");
        Add(DeckActionType.RazerDpi800, "Cat_Razer", "🎯");
        Add(DeckActionType.RazerDpi1600, "Cat_Razer", "🎯");
        Add(DeckActionType.RazerDpi3200, "Cat_Razer", "🎯");
        Add(DeckActionType.RazerDpi6400, "Cat_Razer", "🎯");
        Add(DeckActionType.RazerPollingRateCycle, "Cat_Razer", "⚡");
        Add(DeckActionType.RazerPollingRateUp, "Cat_Razer", "▲");
        Add(DeckActionType.RazerPollingRateDown, "Cat_Razer", "▼");
        Add(DeckActionType.RazerPollingRate125, "Cat_Razer", "⚡");
        Add(DeckActionType.RazerPollingRate500, "Cat_Razer", "⚡");
        Add(DeckActionType.RazerPollingRate1000, "Cat_Razer", "⚡");
        Add(DeckActionType.RazerPollingRate2000, "Cat_Razer", "⚡");
        Add(DeckActionType.RazerPollingRate4000, "Cat_Razer", "⚡");
        Add(DeckActionType.RazerPollingRate8000, "Cat_Razer", "⚡");
        Add(DeckActionType.RazerProfileNext, "Cat_Razer", "🐍");
        Add(DeckActionType.RazerProfilePrev, "Cat_Razer", "🐍");
        Add(DeckActionType.RazerProfile1, "Cat_Razer", "🐍");
        Add(DeckActionType.RazerProfile2, "Cat_Razer", "🐍");
        Add(DeckActionType.RazerProfile3, "Cat_Razer", "🐍");
        Add(DeckActionType.RazerProfile4, "Cat_Razer", "🐍");
        Add(DeckActionType.RazerProfile5, "Cat_Razer", "🐍");

        // Wooting
        Add(DeckActionType.WootingProfileCycle, "Cat_Wooting", "⌨️");
        Add(DeckActionType.WootingProfileNext, "Cat_Wooting", "⌨️");
        Add(DeckActionType.WootingProfilePrev, "Cat_Wooting", "⌨️");
        Add(DeckActionType.WootingProfileDigital, "Cat_Wooting", "⌨️");
        Add(DeckActionType.WootingProfile1, "Cat_Wooting", "⌨️");
        Add(DeckActionType.WootingProfile2, "Cat_Wooting", "⌨️");
        Add(DeckActionType.WootingProfile3, "Cat_Wooting", "⌨️");

        // Navigation
        Add(DeckActionType.GoToProfile, "Cat_Nav", "🔀");
        Add(DeckActionType.GoToPage, "Cat_Nav", "📄");
        Add(DeckActionType.NextPage, "Cat_Nav", "▶");
        Add(DeckActionType.PreviousPage, "Cat_Nav", "◀");
        Add(DeckActionType.OpenFolder, "Cat_Nav", "📁");
        Add(DeckActionType.GoBack, "Cat_Nav", "⬅️");
        Add(DeckActionType.GoHome, "Cat_Nav", "🏠");

        // Smart Home: Home Assistant
        Add(DeckActionType.HomeAssistantToggle, "Cat_SmartHome", "💡");
        Add(DeckActionType.HomeAssistantTurnOn, "Cat_SmartHome", "💡");
        Add(DeckActionType.HomeAssistantTurnOff, "Cat_SmartHome", "🔌");
        Add(DeckActionType.HomeAssistantCallService, "Cat_SmartHome", "⚙️");

        // Elgato Stream Deck Plugins
        if (_streamDeckHost != null)
        {
            foreach (var plugin in _streamDeckHost.InstalledPlugins)
            {
                string catName = string.IsNullOrWhiteSpace(plugin.Manifest.Category)
                    ? $"Stream Deck: {plugin.Name}"
                    : plugin.Manifest.Category;

                foreach (var act in plugin.Manifest.Actions)
                {
                    string? iconPath = null;
                    if (!string.IsNullOrEmpty(act.Icon))
                    {
                        string p1 = Path.Combine(plugin.FolderPath, act.Icon);
                        if (File.Exists(p1)) iconPath = p1;
                        else if (File.Exists(p1 + ".png")) iconPath = p1 + ".png";
                        else if (File.Exists(p1 + "@2x.png")) iconPath = p1 + "@2x.png";
                        else if (File.Exists(p1 + ".svg")) iconPath = p1 + ".svg";
                    }

                    if (string.IsNullOrEmpty(iconPath) && !string.IsNullOrEmpty(plugin.IconPath) && File.Exists(plugin.IconPath))
                    {
                        iconPath = plugin.IconPath;
                    }

                    Bitmap? iconBmp = null;
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        iconBmp = KeyItemViewModel.LoadMediaBitmap(iconPath);
                    }

                    bool showTitle = act.States.Count > 0 ? act.States[0].ShowTitle : true;
                    _allItems.Add(new(
                        act.Name,
                        catName,
                        "🔌",
                        ManipulatorType.Key,
                        KeyAction: DeckActionType.StreamDeckPluginAction,
                        Description: act.Tooltip ?? plugin.Description,
                        StreamDeckPluginUuid: plugin.PluginUuid,
                        StreamDeckActionUuid: act.UUID,
                        IconImagePath: iconPath,
                        IconBitmap: iconBmp,
                        ShowTitle: showTitle));
                }
            }
        }
    }

    private void LoadSliderCatalog()
    {
        string catMain = LocalizationService.Instance.GetString("Cat_MainAudio", "Audio Główne");
        string catApps = LocalizationService.Instance.GetString("Cat_Applications", "Aplikacje");

        _allItems.Add(new(LocalizationService.Instance.GetString("Fader_Master", "Główna głośność"), catMain, "🔊", ManipulatorType.Slider, SliderTargetType: AudioTargetType.MasterPlayback));
        _allItems.Add(new(LocalizationService.Instance.GetString("Fader_Mic", "Głośność mikrofonu"), catMain, "🎙️", ManipulatorType.Slider, SliderTargetType: AudioTargetType.MasterRecording));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_HomeAssistantLightBrightness", "💡 Home Assistant: Jasność światła"), catMain, "💡", ManipulatorType.Slider, SliderTargetType: AudioTargetType.HomeAssistantLight));
        _allItems.Add(new(LocalizationService.Instance.GetString("Fader_None", "Brak (Nieprzypisany)"), catMain, "⚪", ManipulatorType.Slider, SliderTargetType: AudioTargetType.None));

        try
        {
            var sessions = _audioService?.GetActiveAudioSessions();
            if (sessions != null && sessions.Count > 0)
            {
                foreach (var s in sessions)
                {
                    if (string.IsNullOrWhiteSpace(s.ProcessName)) continue;
                    string icon = s.ProcessName.ToLowerInvariant() switch
                    {
                        var p when p.Contains("spotify") => "🎵",
                        var p when p.Contains("discord") => "💬",
                        var p when p.Contains("chrome") || p.Contains("edge") || p.Contains("firefox") => "🌐",
                        var p when p.Contains("steam") => "🎮",
                        _ => "🔊"
                    };
                    _allItems.Add(new(s.DisplayName, catApps, icon, ManipulatorType.Slider, SliderTargetType: AudioTargetType.Process, SliderProcessName: s.ProcessName));
                }
            }
            else
            {
                _allItems.Add(new("Spotify", catApps, "🎵", ManipulatorType.Slider, SliderTargetType: AudioTargetType.Process, SliderProcessName: "Spotify"));
                _allItems.Add(new("Discord", catApps, "💬", ManipulatorType.Slider, SliderTargetType: AudioTargetType.Process, SliderProcessName: "Discord"));
            }
        }
        catch
        {
            _allItems.Add(new("Spotify", catApps, "🎵", ManipulatorType.Slider, SliderTargetType: AudioTargetType.Process, SliderProcessName: "Spotify"));
            _allItems.Add(new("Discord", catApps, "💬", ManipulatorType.Slider, SliderTargetType: AudioTargetType.Process, SliderProcessName: "Discord"));
        }
    }

    private void LoadRollerCatalog()
    {
        string catAudio = LocalizationService.Instance.GetString("Cat_Audio", "Audio");
        string catNav = LocalizationService.Instance.GetString("Cat_Nav", "Nawigacja");
        string catMedia = LocalizationService.Instance.GetString("Cat_Media", "Media");
        string catRazer = LocalizationService.Instance.GetString("Cat_Razer", "Razer");
        string catWooting = LocalizationService.Instance.GetString("Cat_Wooting", "Wooting");
        string catSmartHome = LocalizationService.Instance.GetString("Cat_SmartHome", "Smart Home");
        string catSystem = LocalizationService.Instance.GetString("Cat_System", "System");

        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_Volume"), catAudio, "🔊", ManipulatorType.Roller, RollerAction: RollerActionType.MasterVolume));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_ScrollV"), catNav, "↕️", ManipulatorType.Roller, RollerAction: RollerActionType.ScrollVertical));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_ScrollH"), catNav, "↔️", ManipulatorType.Roller, RollerAction: RollerActionType.ScrollHorizontal));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_Media"), catMedia, "⏭️", ManipulatorType.Roller, RollerAction: RollerActionType.MediaTrack));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_RazerDpi"), catRazer, "🎯", ManipulatorType.Roller, RollerAction: RollerActionType.RazerDpiCycle));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_RazerProfiles"), catRazer, "🐍", ManipulatorType.Roller, RollerAction: RollerActionType.RazerProfileCycle));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_RazerPolling"), catRazer, "⚡", ManipulatorType.Roller, RollerAction: RollerActionType.RazerPollingRateCycle));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_WootingProfiles"), catWooting, "⌨️", ManipulatorType.Roller, RollerAction: RollerActionType.WootingProfileCycle));
        _allItems.Add(new(LocalizationService.Instance.GetString("Roller_HomeAssistantLightBrightness", "💡 Home Assistant: Jasność światła"), catSmartHome, "💡", ManipulatorType.Roller, RollerAction: RollerActionType.HomeAssistantLightBrightness));
        _allItems.Add(new(LocalizationService.Instance.GetString("Action_None"), catSystem, "⚪", ManipulatorType.Roller, RollerAction: RollerActionType.None));
    }

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    [RelayCommand]
    public void ChooseAction(ActionCatalogItem item)
    {
        ActionSelected?.Invoke(item);
    }

    [RelayCommand]
    public void SelectCategory(string category)
    {
        SearchQuery = "";
        SelectedCategory = category;
        foreach (var c in Categories)
        {
            c.IsSelected = c.Name.Equals(category, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var items = _allItems.AsEnumerable();
        string allCat = LocalizationService.Instance.GetString("Cat_All", "Wszystkie");

        if (!string.IsNullOrWhiteSpace(SelectedCategory) &&
            !SelectedCategory.Equals(allCat, StringComparison.OrdinalIgnoreCase) &&
            !SelectedCategory.Equals("Wszystkie", StringComparison.OrdinalIgnoreCase) &&
            !SelectedCategory.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(x => x.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            items = items.Where(x => x.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                     x.Category.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in items)
        {
            FilteredItems.Add(item);
        }
    }
}

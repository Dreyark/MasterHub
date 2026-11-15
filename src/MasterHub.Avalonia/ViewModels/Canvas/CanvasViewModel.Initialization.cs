using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{

    private bool _suppressAudioOnStartup = true;

    /// <summary>For tests: the first 3 s after start do not touch Windows audio.</summary>
    internal bool SuppressAudioOnStartup { get => _suppressAudioOnStartup; set => _suppressAudioOnStartup = value; }

    public CanvasViewModel(
        IMasterHubDeviceService deviceService,
        IKeyRenderer keyRenderer,
        IAnimatedKeyRenderer animatedKeyRenderer,
        ISystemControlService systemControl,
        IAudioService audioService,
        IRazerService razerService,
        IWootingService wootingService,
        ISystemMonitorService systemMonitorService,
        INowPlayingService nowPlayingService,
        IObsService obsService,
        IDiscordService discordService,
        ITeamsService teamsService,
        ITwitchService twitchService,
        IKickService kickService,
        IJetKvmService jetKvmService,
        ISpotifyService spotifyService,
        IVlcService vlcService,
        IVoicemodService voicemodService,
        IYouTubeService youtubeService,
        IStreamlabsService streamlabsService,
        IProfileService profileService,
        IKeyInteractionEngine? keyInteractionEngine = null,
        IStreamDeckPluginHost? streamDeckPluginHost = null,
        IHomeAssistantService? homeAssistantService = null,
        MasterHub.Core.Dialogs.IConfirmDialogService? confirmDialog = null,
        MasterHub.Core.Audio.IAudioDeviceService? audioDevices = null,
        MasterHub.Core.LiveValues.ILiveValueService? liveValues = null,
        MasterHub.Core.Devices.DeckStatusLightService? statusLights = null,
        MasterHub.Core.Timers.ITimerService? timers = null,
        MasterHub.Core.Audio.ISoundboardService? soundboard = null,
        MasterHub.Core.Profiles.Auto.IPressCounter? pressCounter = null,
        MasterHub.Core.Status.IStatusBoard? statusBoard = null)
    {
        _pressCounter = pressCounter;
        _statusBoard = statusBoard;
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _keyRenderer = keyRenderer ?? throw new ArgumentNullException(nameof(keyRenderer));
        _animatedKeyRenderer = animatedKeyRenderer ?? throw new ArgumentNullException(nameof(animatedKeyRenderer));
        _systemControl = systemControl ?? throw new ArgumentNullException(nameof(systemControl));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _razerService = razerService ?? throw new ArgumentNullException(nameof(razerService));
        _wootingService = wootingService ?? throw new ArgumentNullException(nameof(wootingService));
        _systemMonitorService = systemMonitorService ?? throw new ArgumentNullException(nameof(systemMonitorService));
        _nowPlayingService = nowPlayingService ?? throw new ArgumentNullException(nameof(nowPlayingService));
        _obsService = obsService ?? throw new ArgumentNullException(nameof(obsService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _jetKvmService = jetKvmService ?? throw new ArgumentNullException(nameof(jetKvmService));
        _spotifyService = spotifyService ?? throw new ArgumentNullException(nameof(spotifyService));
        _vlcService = vlcService ?? throw new ArgumentNullException(nameof(vlcService));
        _voicemodService = voicemodService ?? throw new ArgumentNullException(nameof(voicemodService));
        _youtubeService = youtubeService ?? throw new ArgumentNullException(nameof(youtubeService));
        _streamlabsService = streamlabsService ?? throw new ArgumentNullException(nameof(streamlabsService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _homeAssistantService = homeAssistantService;
        if (_homeAssistantService != null)
        {
            _homeAssistantService.EntityStateChanged += OnHomeAssistantEntityStateChanged;
        }
        _streamDeckPluginHost = streamDeckPluginHost;
        _confirmDialog = confirmDialog ?? new MasterHub.Core.Dialogs.AutoConfirmDialogService();
        _audioDevices = audioDevices;
        _liveValues = liveValues;
        _timers = timers ?? new MasterHub.Core.Timers.TimerService();
        InitializeTimers();
        _ownsSoundboard = soundboard == null;
        _soundboard = soundboard ?? new MasterHub.Core.Audio.SoundboardService(new MasterHub.Core.Audio.NAudioSoundOutputFactory(), audioDevices);
        InitializeSoundboard();
        _statusLights = statusLights;
        if (_statusLights != null) { _statusLights.Changed += OnStatusLightChanged; _statusLights.Start(); }
        if (_liveValues != null) _liveValues.ReadingChanged += OnLiveReadingChanged;
        _keyInteractionEngine = keyInteractionEngine ?? new KeyInteractionEngine();
        _keyInteractionEngine.KeyTriggered += OnKeyTriggered;
        _keyInteractionEngine.GestureTriggered += OnGestureTriggered;
        var gestureSettings = _profileService.GetAppSettings();
        ApplyGestureTimes(gestureSettings.HoldDurationMs, gestureSettings.DoublePressIntervalMs);
        ApplyVolumeOsdSetting(gestureSettings.VolumeOsdForFaders);   // T-17 g
        _baseStation = new BaseStationItemViewModel();
        _dial = new DialItemViewModel();

        if (_streamDeckPluginHost != null)
        {
            _streamDeckPluginHost.KeyImageUpdated += OnStreamDeckKeyImageUpdated;
            _streamDeckPluginHost.KeyTitleUpdated += OnStreamDeckKeyTitleUpdated;
            _streamDeckPluginHost.KeyStateUpdated += OnStreamDeckKeyStateUpdated;
            _streamDeckPluginHost.KeyAlertRequested += OnStreamDeckKeyAlertRequested;
            _streamDeckPluginHost.KeyOkRequested += OnStreamDeckKeyOkRequested;
            _streamDeckPluginHost.KeySettingsUpdated += OnStreamDeckKeySettingsUpdated;
            _streamDeckPluginHost.SwitchToProfileRequested += OnStreamDeckSwitchToProfileRequested;
        }

        InitializeHardwareCollections();
        WireHardwareEvents();
        WireRenderingPipeline();
        WireMonitoringPipeline();

        UpdateConnectionStates();
        _deviceService.DevicesUpdated += UpdateConnectionStates;
        _deviceService.StandbyStateChanged += OnStandbyForPanorama;

        // Start with no element selected so first button is not highlighted on startup
        SelectedElement = null;

        // Tłumienie audio na start - przez pierwsze 3 sekundy inicjalizacji USB audio Windows nie jest modyfikowane
        var startupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        startupTimer.Tick += (s, e) =>
        {
            _suppressAudioOnStartup = false;
            startupTimer.Stop();
        };
        startupTimer.Start();

        // Automatyczny zapis bieżącego profilu po modyfikacji w UI
        _autoSaveTimer.Tick += (s, e) =>
        {
            _autoSaveTimer.Stop();
            SaveCurrentStateToActiveProfile();
        };

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(string lang)
    {
        NotifyNavProperties();
        foreach (var key in Keys)
        {
            RenderKeyVisual(key, sendToHardware: true);
        }
        foreach (var roller in Rollers)
        {
            roller.NotifyLanguageChanged();
        }
        foreach (var knob in Knobs)
        {
            knob.NotifyLanguageChanged();
        }
        Dial?.NotifyLanguageChanged();
        foreach (var slider in Sliders)
        {
            slider.NotifyLanguageChanged();
        }
        UpdateRotationDisplayState(_deviceService.EffectiveRotationAngle);
    }

    private bool _wasKeypadConnected;
    private bool _wasSliderConnected;
    private bool _wasBaseConnected;
    private bool _wasRollerConnected;
    private bool _wasKnobConnected;
    private bool _wasDialConnected;

    private void UpdateConnectionStates()
    {
        bool keypadNow = _deviceService.IsKeypadConnected;
        bool sliderNow = _deviceService.IsSliderConnected;
        bool rollerNow = _deviceService.IsRollerConnected;
        bool baseNow = _deviceService.IsBaseConnected;
        bool knobNow = _deviceService.IsKnobConnected;
        bool dialNow = _deviceService.IsDialConnected;

        bool keypadReconnected = keypadNow && !_wasKeypadConnected;
        bool sliderReconnected = sliderNow && !_wasSliderConnected;
        bool baseReconnected = baseNow && !_wasBaseConnected;
        bool rollerReconnected = rollerNow && !_wasRollerConnected;
        bool knobReconnected = knobNow && !_wasKnobConnected;
        bool dialReconnected = dialNow && !_wasDialConnected;

        _wasKeypadConnected = keypadNow;
        _wasSliderConnected = sliderNow;
        _wasBaseConnected = baseNow;
        _wasRollerConnected = rollerNow;
        _wasKnobConnected = knobNow;
        _wasDialConnected = dialNow;

        IsKeypadConnected = keypadNow;
        IsSliderConnected = sliderNow;
        IsRollerConnected = rollerNow;
        IsBaseConnected = baseNow;
        IsKnobConnected = knobNow;
        IsDialConnected = dialNow;

        if (keypadReconnected || sliderReconnected || baseReconnected || rollerReconnected || knobReconnected || dialReconnected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                PushAllHardwareState();
            });
        }
    }

    [RelayCommand]
    public void SetFilter(string filter)
    {
        ActiveFilter = filter;
    }

    partial void OnActiveFilterChanged(string value)
    {
        OnPropertyChanged(nameof(ShowKeys));
        OnPropertyChanged(nameof(ShowSliders));
        OnPropertyChanged(nameof(ShowRollers));
        OnPropertyChanged(nameof(ShowKnobs));
        OnPropertyChanged(nameof(ShowDial));
        OnPropertyChanged(nameof(ShowBase));
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterKeys));
        OnPropertyChanged(nameof(IsFilterSliders));
        OnPropertyChanged(nameof(IsFilterRollers));
        OnPropertyChanged(nameof(IsFilterBase));
    }

    [RelayCommand]
    public void DeselectAll()
    {
        ClearSelection();
        SelectedElement = null;
        NotifySelectionChanged();
    }

    private void InitializeHardwareCollections()
    {
        // 15 Klawiszy LCD (3 wiersze x 5 kolumn)
        for (int i = 0; i < 15; i++)
        {
            var key = new KeyItemViewModel
            {
                Index = i,
                Title = $"Klawisz {i + 1}"
            };
            key.ActionChangedCallback = OnKeyChanged;
            key.ApplyPanoramicWallpaperRequested = (path, fitMode) => ApplyPanoramicWallpaper(path, fitMode);
            key.ClearPanoramicWallpaperRequested = () => ClearPanoramicWallpaper();
            key.PanoramicTransformRequested = (scale, x, y) => ApplyPanoramicTransform(scale, x, y);
            key.NavigationRequestedCallback = (k, act, p) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    switch (act)
                    {
                        case DeckActionType.OpenFolder:
                            OpenFolder(p ?? "");
                            break;
                        case DeckActionType.GoBack:
                            NavigateBack();
                            break;
                        case DeckActionType.GoHome:
                            NavigateHome();
                            break;
                        case DeckActionType.GoToPage:
                            SwitchToPage(ParsePageIndex(p));
                            break;
                        case DeckActionType.NextPage:
                            NextPage();
                            break;
                        case DeckActionType.PreviousPage:
                            PreviousPage();
                            break;
                    }
                });
            };
            key.FolderDeletedCallback = (target) => DeleteFolder(target);
            Keys.Add(key);
        }

        // 5 Suwaków zmotoryzowanych Audio
        for (int i = 0; i < 5; i++)
        {
            var slider = new SliderItemViewModel
            {
                Index = i,
                Percent = 100,
                ColorHex = "#38BDF8",
                Brightness = 100
            };
            slider.ClearTarget(); // Domyślnie brak przypisania - ŻADEN suwak nie modyfikuje audio samowolnie
            slider.ColorChangedCallback = OnSliderColorChanged;
            slider.BrightnessChangedCallback = OnSliderBrightnessChanged;
            slider.TargetChangedCallback = OnSliderTargetChanged;
            Sliders.Add(slider);
        }

        // 2 Rolki / Enkodery obrotowe (0 = Lewa, 1 = Prawa)
        Rollers.Add(new RollerItemViewModel
        {
            Index = 0,
            AssignedAction = RollerActionType.MasterVolume,
            ActionChangedCallback = OnRollerActionChanged,
            ColorChangedCallback = OnRollerColorChanged,
            BrightnessChangedCallback = OnRollerBrightnessChanged
        });
        Rollers.Add(new RollerItemViewModel
        {
            Index = 1,
            AssignedAction = RollerActionType.ScrollVertical,
            ActionChangedCallback = OnRollerActionChanged,
            ColorChangedCallback = OnRollerColorChanged,
            BrightnessChangedCallback = OnRollerBrightnessChanged
        });

        // Podstawa dock
        BaseStation.ColorHex = "#5E81F4";
        BaseStation.Brightness = 100;
        BaseStation.ColorChangedCallback = OnBaseColorChanged;
        BaseStation.BrightnessChangedCallback = OnBaseBrightnessChanged;
        BaseStation.EffectChangedCallback = OnBaseEffectChanged;
        BaseStation.StatusLightsChangedCallback = OnStatusLightSettingsChanged;
        BaseStation.LoadStatusLights(_profileService.GetAppSettings().StatusLights);

        // 3 Pokrętła (Knobs)
        for (int i = 0; i < 3; i++)
        {
            var knob = new KnobItemViewModel
            {
                Index = i,
                RotateAction = RollerActionType.MasterVolume,
                PressAction = DeckActionType.MasterMute,
                ColorHex = "#A855F7",
                Brightness = 100,
                Speed = 50,
                LedMode = KnobLedMode.Static
            };
            knob.ActionChangedCallback = OnKnobActionChanged;
            knob.ColorChangedCallback = OnKnobColorChanged;
            knob.BrightnessChangedCallback = OnKnobBrightnessChanged;
            knob.LedModeChangedCallback = OnKnobLedModeChanged;
            Knobs.Add(knob);
        }

        // 1 Dial IPS
        Dial = new DialItemViewModel
        {
            RotateAction = RollerActionType.MasterVolume,
            PressAction = DeckActionType.MediaPlayPause,
            Brightness = 100,
            ColorHex = "#06B6D4"
        };
        Dial.ActionChangedCallback = OnDialActionChanged;
        Dial.ColorChangedCallback = OnDialColorChanged;
        Dial.BrightnessChangedCallback = OnDialBrightnessChanged;
        Dial.ImageChangedCallback = OnDialImageChanged;
    }
}

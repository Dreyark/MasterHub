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

/// <summary>
/// ViewModel zarządzający kanwą modułów sprzętowych MasterHub (siatka klawiszy LCD, suwaki, rolki, stacja dokująca).
/// Integruje zdarzenia sprzętowe oraz potok renderowania grafik LCD SkiaSharp.
/// </summary>
public partial class CanvasViewModel : ObservableObject, IDisposable
{
    private readonly IMasterHubDeviceService _deviceService;
    private readonly IKeyRenderer _keyRenderer;
    private readonly IAnimatedKeyRenderer _animatedKeyRenderer;
    private readonly ISystemControlService _systemControl;
    private readonly IAudioService _audioService;
    private readonly IRazerService _razerService;
    private readonly IWootingService _wootingService;
    private readonly ISystemMonitorService _systemMonitorService;
    private readonly INowPlayingService _nowPlayingService;
    private readonly IObsService _obsService;
    private readonly IDiscordService _discordService;
    private readonly ITeamsService _teamsService;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IJetKvmService _jetKvmService;
    private readonly ISpotifyService _spotifyService;
    private readonly IVlcService _vlcService;
    private readonly IVoicemodService _voicemodService;
    private readonly IYouTubeService _youtubeService;
    private readonly IStreamlabsService _streamlabsService;
    private readonly IProfileService _profileService;
    private readonly IHomeAssistantService? _homeAssistantService;
    private readonly IKeyInteractionEngine _keyInteractionEngine;
    private readonly MasterHub.Core.Dialogs.IConfirmDialogService _confirmDialog;
    private readonly MasterHub.Core.Audio.IAudioDeviceService? _audioDevices;
    // Klawisz, którego akcja zasilania została właśnie potwierdzona przytrzymaniem (patrz PowerActionGuard).
    private KeyItemViewModel? _holdConfirmedKey;
    private readonly IStreamDeckPluginHost? _streamDeckPluginHost;
    private readonly KeyItemViewModel _virtualActionKey = new() { Index = -1 };
    private bool _disposed;

    public ObservableCollection<KeyItemViewModel> Keys { get; } = new();
    public ObservableCollection<SliderItemViewModel> Sliders { get; } = new();
    public ObservableCollection<RollerItemViewModel> Rollers { get; } = new();
    public ObservableCollection<KnobItemViewModel> Knobs { get; } = new();

    [ObservableProperty]
    private BaseStationItemViewModel _baseStation;

    [ObservableProperty]
    private DialItemViewModel _dial;

    [ObservableProperty]
    private object? _selectedElement;

    [ObservableProperty]
    private string _activeFilter = "Wszystko"; // "Wszystko", "Przyciski", "Suwaki", "Pokrętła", "Podstawka"

    public bool ShowKeys => ActiveFilter == "Wszystko" || ActiveFilter == "Przyciski";
    public bool ShowSliders => ActiveFilter == "Wszystko" || ActiveFilter == "Suwaki";
    public bool ShowRollers => ActiveFilter == "Wszystko" || ActiveFilter == "Pokrętła";
    public bool ShowKnobs => IsKnobConnected && (ActiveFilter == "Wszystko" || ActiveFilter == "Pokrętła" || ActiveFilter == "3 Knobs" || ActiveFilter == "All" || ActiveFilter == "Rollers" || ActiveFilter == "Knobs");
    public bool ShowDial => IsDialConnected && (ActiveFilter == "Wszystko" || ActiveFilter == "Pokrętła" || ActiveFilter == "Dial IPS" || ActiveFilter == "All" || ActiveFilter == "Rollers" || ActiveFilter == "Dial");
    public bool ShowBase => ActiveFilter == "Wszystko" || ActiveFilter == "Podstawka";

    public bool IsFilterAll => ActiveFilter == "Wszystko";
    public bool IsFilterKeys => ActiveFilter == "Przyciski";
    public bool IsFilterSliders => ActiveFilter == "Suwaki";
    public bool IsFilterRollers => ActiveFilter == "Pokrętła";
    public bool IsFilterBase => ActiveFilter == "Podstawka";

    public bool IsKeySelected => SelectedElement is KeyItemViewModel;
    public bool IsSliderSelected => SelectedElement is SliderItemViewModel;
    public bool IsRollerSelected => SelectedElement is RollerItemViewModel;
    public bool IsKnobSelected => SelectedElement is KnobItemViewModel;
    public bool IsDialSelected => SelectedElement is DialItemViewModel;
    public bool IsBaseSelected => SelectedElement is BaseStationItemViewModel;

    [ObservableProperty]
    private byte _keypadBrightness = 100;

    [ObservableProperty]
    private bool _isSyncBrightness = false;

    [ObservableProperty]
    private DeckRotationMode _rotationMode = DeckRotationMode.Auto;

    [ObservableProperty]
    private int _manualRotationAngle = 0;

    [ObservableProperty]
    private int _keypadColumns = 5;

    [ObservableProperty]
    private int _keypadRows = 3;

    [ObservableProperty]
    private string _orientationStatusText = "0° (Pozioma) [Auto]";

    [ObservableProperty]
    private bool _isPortraitLayout = false;

    [ObservableProperty]
    private DeckWallpaperMode _wallpaperMode = DeckWallpaperMode.PerKey;

    [ObservableProperty]
    private string? _panoramicWallpaperPath;

    [ObservableProperty]
    private string _panoramicWallpaperFitMode = "Cover";

    [ObservableProperty]
    private int _panoramicWallpaperScale = 100;

    [ObservableProperty]
    private int _panoramicWallpaperOffsetX;

    [ObservableProperty]
    private int _panoramicWallpaperOffsetY;

    public bool IsPanoramicWallpaperActive => WallpaperMode == DeckWallpaperMode.PanoramicDeck && !string.IsNullOrWhiteSpace(PanoramicWallpaperPath);

    private bool _isSyncing;
    private volatile bool _isUiSuspended;

    public bool IsUiSuspended
    {
        get => _isUiSuspended;
        set
        {
            if (_isUiSuspended == value) return;
            _isUiSuspended = value;
            if (!value) RefreshAllKeyVisuals();
        }
    }

    public void RefreshAllKeyVisuals()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsPanoramicWallpaperActive && !string.IsNullOrEmpty(PanoramicWallpaperPath) && System.IO.File.Exists(PanoramicWallpaperPath))
            {
                RestartPanorama();
                return;
            }

            for (int i = 0; i < Keys.Count; i++)
            {
                RenderKeyVisual(Keys[i], sendToHardware: false);
            }
        });
    }

    public void SyncAllBrightness(byte brightness, string source)
    {
        if (_isSyncing) return;
        _isSyncing = true;
        try
        {
            if (source != "Keypad") KeypadBrightness = brightness;
            if (source != "Base") BaseStation.Brightness = brightness;
            foreach (var s in Sliders)
            {
                s.Brightness = brightness;
            }
            foreach (var r in Rollers)
            {
                r.Brightness = brightness;
            }
            _deviceService.SetKeypadBacklight(brightness);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnKeypadBrightnessChanged(byte value)
    {
        _deviceService.SetKeypadBacklight(value);
        if (IsSyncBrightness)
        {
            SyncAllBrightness(value, "Keypad");
        }
        ScheduleAutoSave();
    }

    private readonly DispatcherTimer _autoSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _isApplyingProfile;
    private bool _hasLoadedProfile;

    /// <summary>Id of the profile the canvas shows right now. Saving goes to <i>this</i> profile, not to whichever one is active by then.</summary>
    private string? _loadedProfileId;

    public void ScheduleAutoSave()
    {
        if (_isApplyingProfile) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    [ObservableProperty]
    private bool _isKeypadConnected;

    [ObservableProperty]
    private bool _isSliderConnected;

    [ObservableProperty]
    private bool _isRollerConnected;

    [ObservableProperty]
    private bool _isBaseConnected;

    [ObservableProperty]
    private bool _isKnobConnected;

    [ObservableProperty]
    private bool _isDialConnected;

    partial void OnIsKnobConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowKnobs));
        if (!value && SelectedElement is KnobItemViewModel)
        {
            SelectedElement = null;
        }
    }

    partial void OnIsDialConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDial));
        if (!value && SelectedElement is DialItemViewModel)
        {
            SelectedElement = null;
        }
    }
}

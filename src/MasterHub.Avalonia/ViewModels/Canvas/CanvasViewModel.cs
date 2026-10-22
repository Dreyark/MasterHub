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
    private readonly IStreamDeckPluginHost? _streamDeckPluginHost;
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
                ApplyPanoramicWallpaper(PanoramicWallpaperPath, PanoramicWallpaperFitMode);
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

    public KeyItemViewModel? SelectedKey => SelectedElement as KeyItemViewModel;
    public SliderItemViewModel? SelectedSlider => SelectedElement as SliderItemViewModel;
    public RollerItemViewModel? SelectedRoller => SelectedElement as RollerItemViewModel;
    public KnobItemViewModel? SelectedKnob => SelectedElement as KnobItemViewModel;
    public DialItemViewModel? SelectedDial => SelectedElement as DialItemViewModel;
    public BaseStationItemViewModel? SelectedBase => SelectedElement as BaseStationItemViewModel;

    private int _currentPageIndex = 0;
    private int _pageRenderVersion = 0;
    private string? _currentFolderId = null;
    private string _currentFolderName = "";
    private readonly Stack<(int PageIndex, string? FolderId, string FolderName)> _navStack = new();

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        private set
        {
            if (SetProperty(ref _currentPageIndex, value))
            {
                NotifyNavProperties();
            }
        }
    }

    public string? CurrentFolderId
    {
        get => _currentFolderId;
        private set
        {
            if (SetProperty(ref _currentFolderId, value))
            {
                NotifyNavProperties();
            }
        }
    }

    public string CurrentFolderName
    {
        get => _currentFolderName;
        private set
        {
            if (SetProperty(ref _currentFolderName, value))
            {
                NotifyNavProperties();
            }
        }
    }

    public bool IsInsideFolder => !string.IsNullOrEmpty(CurrentFolderId);

    public int TotalPagesCount
    {
        get
        {
            var p = _profileService.GetActiveProfile();
            return p?.Pages != null && p.Pages.Count > 0 ? p.Pages.Count : 1;
        }
    }

    public bool CanGoPreviousPage => !IsInsideFolder && TotalPagesCount > 1;
    public bool CanGoNextPage => !IsInsideFolder && TotalPagesCount > 1;
    public bool CanDeleteCurrentPage => !IsInsideFolder && TotalPagesCount > 1;

    public string CurrentPageDisplayName
    {
        get
        {
            if (IsInsideFolder)
            {
                return $"📁 {CurrentFolderName}";
            }
            return string.Format(LocalizationService.Instance.GetString("Nav_PageOf", "Strona {0} z {1}"), CurrentPageIndex + 1, Math.Max(1, TotalPagesCount));
        }
    }

    public void NotifyNavProperties()
    {
        OnPropertyChanged(nameof(CurrentPageIndex));
        OnPropertyChanged(nameof(CurrentFolderId));
        OnPropertyChanged(nameof(CurrentFolderName));
        OnPropertyChanged(nameof(IsInsideFolder));
        OnPropertyChanged(nameof(TotalPagesCount));
        OnPropertyChanged(nameof(CurrentPageDisplayName));
        OnPropertyChanged(nameof(CanGoPreviousPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(CanDeleteCurrentPage));
    }

    [RelayCommand]
    public void NextPage()
    {
        if (IsInsideFolder) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;
        EnsurePagesInitialized(prof);
        if (prof.Pages.Count <= 1) return;

        _animatedKeyRenderer.StopAllAnimations();
        DeselectAll();
        SaveCurrentStateToActiveProfile();
        if (CurrentPageIndex < prof.Pages.Count - 1)
        {
            CurrentPageIndex++;
        }
        else
        {
            CurrentPageIndex = 0;
        }
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public void PreviousPage()
    {
        if (IsInsideFolder) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;
        EnsurePagesInitialized(prof);
        if (prof.Pages.Count <= 1) return;

        _animatedKeyRenderer.StopAllAnimations();
        DeselectAll();
        SaveCurrentStateToActiveProfile();
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
        }
        else
        {
            CurrentPageIndex = prof.Pages.Count - 1;
        }
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public void AddNewPage()
    {
        if (IsInsideFolder) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;
        EnsurePagesInitialized(prof);

        DeselectAll();
        SaveCurrentStateToActiveProfile();
        int newIdx = prof.Pages.Count;
        var newPage = KeyPageModel.Create(newIdx, $"Strona {newIdx + 1}");
        prof.Pages.Add(newPage);
        _profileService.SaveProfile(prof);

        CurrentPageIndex = newIdx;
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public void DeleteCurrentPage()
    {
        if (IsInsideFolder) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null || prof.Pages == null || prof.Pages.Count <= 1) return;

        DeselectAll();
        int toRemove = CurrentPageIndex;
        prof.Pages.RemoveAt(toRemove);
        for (int i = 0; i < prof.Pages.Count; i++)
        {
            prof.Pages[i].PageIndex = i;
            if (prof.Pages[i].Name.StartsWith("Strona "))
            {
                prof.Pages[i].Name = $"Strona {i + 1}";
            }
        }

        CurrentPageIndex = Math.Clamp(toRemove > 0 ? toRemove - 1 : 0, 0, prof.Pages.Count - 1);
        _profileService.SaveProfile(prof);
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public void NavigateBack()
    {
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;

        DeselectAll();
        if (_navStack.Count > 0)
        {
            SaveCurrentStateToActiveProfile();
            var prev = _navStack.Pop();
            CurrentPageIndex = prev.PageIndex;
            CurrentFolderId = prev.FolderId;
            CurrentFolderName = prev.FolderName;
            LoadCurrentViewKeys(prof);
            NotifyNavProperties();
        }
        else if (IsInsideFolder)
        {
            NavigateHome();
        }
        else if (CurrentPageIndex > 0)
        {
            PreviousPage();
        }
    }

    [RelayCommand]
    public void NavigateHome()
    {
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;

        DeselectAll();
        SaveCurrentStateToActiveProfile();
        _navStack.Clear();
        CurrentFolderId = null;
        CurrentFolderName = "";
        CurrentPageIndex = 0;
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    public void SwitchToPage(int pageIndex)
    {
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;
        EnsurePagesInitialized(prof);

        if (pageIndex < 0 || pageIndex >= prof.Pages.Count) return;

        _animatedKeyRenderer.StopAllAnimations();
        DeselectAll();
        SaveCurrentStateToActiveProfile();
        _navStack.Push((CurrentPageIndex, CurrentFolderId, CurrentFolderName));
        CurrentFolderId = null;
        CurrentFolderName = "";
        CurrentPageIndex = pageIndex;
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    public void OpenFolder(string folderNameOrId)
    {
        if (string.IsNullOrWhiteSpace(folderNameOrId))
        {
            folderNameOrId = "Nowy Folder";
        }
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;

        prof.Folders ??= new List<KeyFolderModel>();
        var folder = prof.Folders.FirstOrDefault(f =>
            f.Id.Equals(folderNameOrId, StringComparison.OrdinalIgnoreCase) ||
            f.Name.Equals(folderNameOrId, StringComparison.OrdinalIgnoreCase));

        if (folder == null)
        {
            folder = KeyFolderModel.Create(folderNameOrId, CurrentFolderId);
            prof.Folders.Add(folder);
            _profileService.SaveProfile(prof);
        }

        EnsureFolderKeysInitialized(folder);

        DeselectAll();
        SaveCurrentStateToActiveProfile();
        _navStack.Push((CurrentPageIndex, CurrentFolderId, CurrentFolderName));
        CurrentFolderId = folder.Id;
        CurrentFolderName = folder.Name;
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public void DeleteCurrentFolder()
    {
        if (!IsInsideFolder || string.IsNullOrEmpty(CurrentFolderId)) return;
        DeleteFolder(CurrentFolderId);
    }

    public void DeleteFolder(string folderNameOrId)
    {
        if (string.IsNullOrWhiteSpace(folderNameOrId)) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null || prof.Folders == null) return;

        var folder = prof.Folders.FirstOrDefault(f =>
            f.Id.Equals(folderNameOrId, StringComparison.OrdinalIgnoreCase) ||
            f.Name.Equals(folderNameOrId, StringComparison.OrdinalIgnoreCase));

        if (folder == null) return;

        string targetId = folder.Id;
        string targetName = folder.Name;

        prof.Folders.RemoveAll(f => f.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));

        if (prof.Pages != null)
        {
            foreach (var page in prof.Pages)
            {
                if (page.Keys != null)
                {
                    foreach (var k in page.Keys)
                    {
                        if (k.Action == DeckActionType.OpenFolder &&
                            (k.ActionParameter.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                             k.ActionParameter.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
                        {
                            k.Action = DeckActionType.None;
                            k.ActionParameter = "";
                            k.CustomTitle = "";
                        }
                    }
                }
            }
        }

        if (prof.Keys != null)
        {
            foreach (var k in prof.Keys)
            {
                if (k.Action == DeckActionType.OpenFolder &&
                    (k.ActionParameter.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                     k.ActionParameter.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
                {
                    k.Action = DeckActionType.None;
                    k.ActionParameter = "";
                    k.CustomTitle = "";
                }
            }
        }

        _profileService.SaveProfile(prof);
        DeselectAll();

        if (IsInsideFolder && (CurrentFolderId == targetId || CurrentFolderName.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
        {
            NavigateHome();
        }
        else
        {
            LoadCurrentViewKeys(prof);
            NotifyNavProperties();
        }
    }

    public int ParsePageIndex(string? param)
    {
        if (string.IsNullOrWhiteSpace(param)) return 0;
        var prof = _profileService.GetActiveProfile();
        if (prof?.Pages != null)
        {
            int nameIdx = prof.Pages.FindIndex(p => p.Name.Equals(param.Trim(), StringComparison.OrdinalIgnoreCase));
            if (nameIdx >= 0) return nameIdx;
        }

        var match = System.Text.RegularExpressions.Regex.Match(param, @"\d+");
        if (match.Success && int.TryParse(match.Value, out int pageNum) && pageNum > 0)
        {
            return pageNum - 1;
        }

        return 0;
    }

    private static void EnsurePagesInitialized(ProfileModel prof)
    {
        if (prof.Pages == null || prof.Pages.Count == 0)
        {
            prof.Pages = new List<KeyPageModel>
            {
                new KeyPageModel
                {
                    PageIndex = 0,
                    Name = "Strona 1",
                    Keys = new List<KeyAssignmentModel>(prof.Keys)
                }
            };
        }
    }

    public static void EnsureFolderKeysInitialized(KeyFolderModel folder)
    {
        folder.Keys ??= new List<KeyAssignmentModel>();
        if (folder.Keys.Count == 0 || !folder.Keys.Any(k => k.Index == 0))
        {
            folder.Keys.RemoveAll(k => k.Index == 0);
            folder.Keys.Insert(0, new KeyAssignmentModel
            {
                Index = 0,
                Action = DeckActionType.GoBack,
                CustomTitle = "POWRÓT",
                BackgroundColorHex = "#1E293B",
                TextColorHex = "#FFFFFF"
            });
        }
        for (int i = 1; i < 15; i++)
        {
            if (!folder.Keys.Any(k => k.Index == i))
            {
                folder.Keys.Add(new KeyAssignmentModel { Index = i, Action = DeckActionType.None });
            }
        }
    }

    private bool _suppressAudioOnStartup = true;

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
        IHomeAssistantService? homeAssistantService = null)
    {
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
        _keyInteractionEngine = keyInteractionEngine ?? new KeyInteractionEngine();
        _keyInteractionEngine.KeyTriggered += OnKeyTriggered;
        _baseStation = new BaseStationItemViewModel();
        _dial = new DialItemViewModel();

        if (_streamDeckPluginHost != null)
        {
            _streamDeckPluginHost.KeyImageUpdated += (keyIndex, jpegBytes) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (keyIndex >= 0 && keyIndex < Keys.Count)
                    {
                        var key = Keys[keyIndex];
                        if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                        {
                            key.SetPluginDynamicImage(jpegBytes);
                            RenderKeyVisual(key, sendToHardware: true);
                        }
                    }
                });
            };

            _streamDeckPluginHost.KeyTitleUpdated += (keyIndex, title) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (keyIndex >= 0 && keyIndex < Keys.Count)
                    {
                        var key = Keys[keyIndex];
                        if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                        {
                            key.CustomTitle = title;
                            RenderKeyVisual(key, sendToHardware: true);
                        }
                    }
                });
            };

            _streamDeckPluginHost.KeyStateUpdated += (keyIndex, state) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (keyIndex >= 0 && keyIndex < Keys.Count)
                    {
                        var key = Keys[keyIndex];
                        if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                        {
                            key.IsActiveToggleState = (state != 0);
                            RenderKeyVisual(key, sendToHardware: true);
                        }
                    }
                });
            };

            _streamDeckPluginHost.KeyAlertRequested += (keyIndex) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (keyIndex >= 0 && keyIndex < Keys.Count)
                    {
                        var key = Keys[keyIndex];
                        if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                        {
                            key.IsStreamDeckAlert = true;
                            RenderKeyVisual(key, sendToHardware: true);
                            Task.Delay(1500).ContinueWith(_ =>
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    key.IsStreamDeckAlert = false;
                                    RenderKeyVisual(key, sendToHardware: true);
                                });
                            });
                        }
                    }
                });
            };

            _streamDeckPluginHost.KeyOkRequested += (keyIndex) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (keyIndex >= 0 && keyIndex < Keys.Count)
                    {
                        var key = Keys[keyIndex];
                        if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                        {
                            key.IsStreamDeckOk = true;
                            RenderKeyVisual(key, sendToHardware: true);
                            Task.Delay(1500).ContinueWith(_ =>
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    key.IsStreamDeckOk = false;
                                    RenderKeyVisual(key, sendToHardware: true);
                                });
                            });
                        }
                    }
                });
            };
        }

        InitializeHardwareCollections();
        WireHardwareEvents();
        WireRenderingPipeline();
        WireMonitoringPipeline();

        UpdateConnectionStates();
        _deviceService.DevicesUpdated += UpdateConnectionStates;

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

    private void OnKnobActionChanged(KnobItemViewModel knob) => ScheduleAutoSave();

    private void OnKnobColorChanged(KnobItemViewModel knob, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, knob.Brightness);
            _deviceService.SetKnobLedColor(knob.Index, r, g, b);
        }
        ScheduleAutoSave();
    }

    private void OnKnobBrightnessChanged(KnobItemViewModel knob, byte brightness)
    {
        OnKnobColorChanged(knob, knob.ColorHex);
        _deviceService.SetKnobLedBrightness(knob.Index, brightness);
        ScheduleAutoSave();
    }

    private void OnKnobLedModeChanged(KnobItemViewModel knob, KnobLedMode mode)
    {
        _deviceService.SetKnobLedMode(knob.Index, mode);
        ScheduleAutoSave();
    }

    private void OnDialActionChanged(DialItemViewModel dial) => ScheduleAutoSave();

    private void OnDialColorChanged(DialItemViewModel dial, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            _deviceService.SetDialColor(col.R, col.G, col.B);
        }
        ScheduleAutoSave();
    }

    private void OnDialBrightnessChanged(DialItemViewModel dial, byte brightness)
    {
        _deviceService.SetDialBrightness(brightness);
        ScheduleAutoSave();
    }

    private void OnDialImageChanged(DialItemViewModel dial, string? imagePath)
    {
        if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
        {
            byte[] dialJpeg = ButtonImageRenderer.RenderDialImage(imagePath, "Cover");
            _ = _deviceService.SetDialImageAsync(dialJpeg);
        }
        else
        {
            OnDialColorChanged(dial, dial.ColorHex);
        }
        ScheduleAutoSave();
    }

    private void OnBaseColorChanged(BaseStationItemViewModel baseStation, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, baseStation.Brightness);
            _deviceService.SetBaseLedColor(r, g, b);
        }
        ScheduleAutoSave();
    }

    private void OnBaseBrightnessChanged(BaseStationItemViewModel baseStation, byte brightness)
    {
        OnBaseColorChanged(baseStation, baseStation.ColorHex);
        if (IsSyncBrightness)
        {
            SyncAllBrightness(brightness, "Base");
        }
        ScheduleAutoSave();
    }

    private void OnSliderColorChanged(SliderItemViewModel slider, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, slider.Brightness);
            _deviceService.SetSliderLedColor(slider.Index, r, g, b);
        }
        ScheduleAutoSave();
    }

    private void OnSliderBrightnessChanged(SliderItemViewModel slider, byte brightness)
    {
        OnSliderColorChanged(slider, slider.ColorHex);
        if (IsSyncBrightness)
        {
            SyncAllBrightness(brightness, "Slider");
        }
        ScheduleAutoSave();
    }

    private void OnSliderTargetChanged(SliderItemViewModel slider, string target)
    {
        ScheduleAutoSave();
    }

    private void OnRollerActionChanged(RollerItemViewModel roller)
    {
        ScheduleAutoSave();
    }

    private void OnRollerColorChanged(RollerItemViewModel roller, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, roller.Brightness);
            _deviceService.SetRollerLedColor(roller.Index, r, g, b);
        }
        ScheduleAutoSave();
    }

    private void OnRollerBrightnessChanged(RollerItemViewModel roller, byte brightness)
    {
        OnRollerColorChanged(roller, roller.ColorHex);
        ScheduleAutoSave();
    }

    private void WireHardwareEvents()
    {
        _deviceService.KeyStateChanged += OnKeyStateChanged;
        _deviceService.SliderValueChanged += OnSliderValueChanged;
        _deviceService.RollerRotated += OnRollerRotated;
        _deviceService.KnobRotated += OnKnobRotated;
        _deviceService.KnobPressed += OnKnobPressed;
        _deviceService.DialRotated += OnDialRotated;
        _deviceService.DialPressed += OnDialPressed;
        _deviceService.StandbyStateChanged += OnStandbyStateChanged;
        _deviceService.BaseOrientationChanged += OnBaseOrientationChanged;
        _deviceService.EffectiveRotationChanged += OnEffectiveRotationChanged;
    }

    private void WireRenderingPipeline()
    {
        _keyRenderer.KeyRendered += OnKeyRenderedByService;
        _animatedKeyRenderer.FrameRenderedWithJpeg += OnAnimatedFrameRendered;

        // Inicjalne renderowanie podglądu UI bez zalewania magistrali USB
        for (int i = 0; i < Keys.Count; i++)
        {
            RenderKeyVisual(Keys[i], sendToHardware: false);
        }
    }

    private void WireMonitoringPipeline()
    {
        try
        {
            _systemMonitorService.IsEnabled = true;
            _systemMonitorService.MetricsUpdated += OnMetricsUpdated;

            _nowPlayingService.IsEnabled = true;
            _nowPlayingService.TrackChanged += OnMetricsUpdated;

            _obsService.CurrentProgramSceneChanged += OnObsSceneChanged;
            _obsService.StreamStateChanged += OnObsStreamChanged;
            _obsService.RecordStateChanged += OnObsRecordChanged;
            _obsService.VirtualCamStateChanged += OnObsVirtualCamChanged;
            _obsService.ReplayBufferStateChanged += OnObsReplayBufferChanged;
            _obsService.StudioModeStateChanged += OnObsStudioModeChanged;
            _obsService.ConnectionStateChanged += OnObsConnectionStateChanged;
            _discordService.VoiceStateChanged += OnDiscordVoiceStateChanged;
            _youtubeService.StatusUpdated += OnYouTubeStatusUpdated;
            _twitchService.EmoteOnlyStateChanged += OnTwitchEmoteOnlyChanged;
            _twitchService.SubOnlyStateChanged += OnTwitchSubOnlyChanged;
            _kickService.EmoteOnlyStateChanged += OnKickEmoteOnlyChanged;
            _kickService.SubOnlyStateChanged += OnKickSubOnlyChanged;
            _jetKvmService.PowerStateChanged += OnJetKvmPowerStateChanged;
            _spotifyService.PlaybackStateChanged += OnSpotifyPlaybackStateChanged;
            _vlcService.PlaybackStateChanged += OnVlcPlaybackStateChanged;
            _voicemodService.StateChanged += OnVoicemodStateChanged;
            _voicemodService.ConnectionStateChanged += OnVoicemodConnectionChanged;
            _voicemodService.VoicesLoaded += OnVoicemodVoicesLoaded;
            _voicemodService.SoundsLoaded += OnVoicemodSoundsLoaded;

            // Automatyczne podłączenie do Discord Desktop w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var dcCfg = _profileService.GetDiscordSettings();
                    await _discordService.ConnectAsync(dcCfg.ClientId, dcCfg.AccessToken);
                }
                catch { }
            });

            // Automatyczne podłączenie do OBS Studio WebSocket w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var obsCfg = _profileService.GetObsSettings();
                    if (obsCfg.AutoConnect)
                    {
                        await _obsService.ConnectAsync(obsCfg.Host, obsCfg.Port, obsCfg.Password);
                    }
                }
                catch { }
            });

            // Automatyczne podłączenie do Twitch w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var twitchCfg = _profileService.GetTwitchSettings();
                    if (twitchCfg.AutoConnect && !string.IsNullOrWhiteSpace(twitchCfg.Channel))
                    {
                        await _twitchService.ConnectAsync(twitchCfg.Channel, twitchCfg.OAuthToken, twitchCfg.BotUsername, twitchCfg.ClientId);
                    }
                }
                catch { }
            });

            // Automatyczne podłączenie do Kick w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var kickCfg = _profileService.GetKickSettings();
                    if (kickCfg.AutoConnect && !string.IsNullOrWhiteSpace(kickCfg.Channel))
                    {
                        await _kickService.ConnectAsync(kickCfg.Channel, kickCfg.AuthToken, kickCfg.BotUsername);
                    }
                }
                catch { }
            });

            // Automatyczna konfiguracja i nasłuch JetKVM w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var jetCfg = _profileService.GetJetKvmSettings();
                    _jetKvmService.Configure(jetCfg);
                    if (jetCfg.AutoConnect)
                    {
                        await _jetKvmService.RefreshStateAsync();
                    }
                }
                catch { }
            });

            // Automatyczna konfiguracja i nasłuch Spotify w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var spCfg = _profileService.GetSpotifySettings();
                    _spotifyService.Configure(spCfg);
                    if (spCfg.AutoConnect && !string.IsNullOrWhiteSpace(spCfg.AccessToken))
                    {
                        await _spotifyService.RefreshPlaybackStateAsync();
                    }
                }
                catch { }
            });

            // Automatyczna konfiguracja i nasłuch VLC w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var vlcCfg = _profileService.GetVlcSettings();
                    _vlcService.Configure(vlcCfg);
                    if (vlcCfg.AutoConnect)
                    {
                        await _vlcService.RefreshPlaybackStateAsync();
                    }
                }
                catch { }
            });

            // Automatyczna konfiguracja i połączenie z Voicemod w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var vmCfg = _profileService.GetVoicemodSettings();
                    _voicemodService.Configure(vmCfg);
                    if (vmCfg.AutoConnect)
                    {
                        await _voicemodService.ConnectAsync();
                    }
                }
                catch { }
            });

            // Inicjalne pobranie metryk na start
            foreach (var key in Keys)
            {
                UpdateKeyMonitoringValue(key);
            }
        }
        catch { }
    }

    private int GetGpuIndexByName(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return _systemMonitorService.SelectedGpuIndex;
        var names = _systemMonitorService.AvailableGpuNames;
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], gpuName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return _systemMonitorService.SelectedGpuIndex;
    }

    public void UpdateKeyMonitoringValue(KeyItemViewModel key)
    {
        if (key.AssignedAction == DeckActionType.None) return;

        switch (key.AssignedAction)
        {
            case DeckActionType.DisplayCpuUsage:
                key.LiveValueText = $"{Math.Round(_systemMonitorService.CpuUsagePercent)}%";
                break;
            case DeckActionType.DisplayRamUsage:
                key.LiveValueText = $"{Math.Round(_systemMonitorService.RamUsagePercent)}%";
                break;
            case DeckActionType.DisplayGpuUsage:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    int gpuIdx = GetGpuIndexByName(key.ActionParameter);
                    key.LiveValueText = $"{Math.Round(_systemMonitorService.GetGpuUsagePercent(gpuIdx))}%";
                }
                else
                {
                    key.LiveValueText = $"{Math.Round(_systemMonitorService.GpuUsagePercent)}%";
                }
                break;
            case DeckActionType.DisplayCpuTemp:
                float cpuTemp = _systemMonitorService.CpuTemperatureCelsius;
                key.LiveValueText = cpuTemp > 0 ? $"{Math.Round(cpuTemp)}°C" : $"{Math.Round(_systemMonitorService.GpuTemperatureCelsius)}°C";
                break;
            case DeckActionType.DisplayGpuTemp:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    int gpuIdx = GetGpuIndexByName(key.ActionParameter);
                    key.LiveValueText = $"{Math.Round(_systemMonitorService.GetGpuTemperatureCelsius(gpuIdx))}°C";
                }
                else
                {
                    key.LiveValueText = $"{Math.Round(_systemMonitorService.GpuTemperatureCelsius)}°C";
                }
                break;
            case DeckActionType.DisplayNetworkSpeed:
                key.LiveValueText = FormatNetworkSpeed(
                    _systemMonitorService.NetworkDownloadBytesPerSec,
                    _systemMonitorService.NetworkDownloadMbps,
                    key.ActionParameter);
                break;
            case DeckActionType.DisplayDiskUsage:
                key.LiveValueText = $"{Math.Round(_systemMonitorService.GetDiskUsagePercent(key.ActionParameter))}%";
                break;
            case DeckActionType.DisplayClock:
                key.LiveValueText = DateTime.Now.ToString("HH:mm");
                break;
            case DeckActionType.DisplayDate:
                key.LiveValueText = DateTime.Now.ToString("dd MMM");
                break;
            case DeckActionType.DisplayFps:
                key.LiveValueText = $"{Math.Round(_systemMonitorService.CurrentFps)} FPS";
                break;
            case DeckActionType.NowPlaying:
                key.LiveValueText = _nowPlayingService?.DisplayTrack ?? "";
                break;
        }
    }

    public static string FormatNetworkSpeed(double bytesPerSec, float fallbackMbps, string? unitPreference)
    {
        bool isBitMode = unitPreference != null && (
            unitPreference.Contains("Mb/s", StringComparison.Ordinal) ||
            unitPreference.Contains("Kb/s", StringComparison.Ordinal) ||
            unitPreference.Contains("Gb/s", StringComparison.Ordinal) ||
            unitPreference.Contains("b/s", StringComparison.Ordinal) ||
            unitPreference.Contains("bit", StringComparison.OrdinalIgnoreCase) ||
            unitPreference.Contains("bps", StringComparison.OrdinalIgnoreCase));

        if (isBitMode)
        {
            double bitsPerSec = (bytesPerSec > 0) ? (bytesPerSec * 8.0) : (fallbackMbps * 1_000_000.0);
            if (bitsPerSec >= 1_000_000_000.0)
                return $"{bitsPerSec / 1_000_000_000.0:0.##} Gb/s";
            if (bitsPerSec >= 1_000_000.0)
                return $"{bitsPerSec / 1_000_000.0:0.#} Mb/s";
            if (bitsPerSec >= 1000.0)
                return $"{bitsPerSec / 1000.0:0.#} Kb/s";
            return $"{bitsPerSec:0} b/s";
        }
        else
        {
            double bytes = (bytesPerSec > 0) ? bytesPerSec : (fallbackMbps * 1_000_000.0 / 8.0);
            if (bytes >= 1024.0 * 1024.0 * 1024.0)
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB/s";
            if (bytes >= 1024.0 * 1024.0)
                return $"{bytes / (1024.0 * 1024.0):0.#} MB/s";
            if (bytes >= 1024.0)
                return $"{bytes / 1024.0:0.#} KB/s";
            return $"{bytes:0} B/s";
        }
    }

    private int _discordReconnectCounter;

    private void OnMetricsUpdated()
    {
        _discordReconnectCounter++;
        if (_discordReconnectCounter >= 10)
        {
            _discordReconnectCounter = 0;
            if (!_discordService.IsConnected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var dc = _profileService.GetDiscordSettings();
                        await _discordService.ConnectAsync(dc.ClientId, dc.AccessToken);
                    }
                    catch { }
                });
            }
            if (!_obsService.IsConnected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var obs = _profileService.GetObsSettings();
                        if (obs.AutoConnect)
                        {
                            await _obsService.ConnectAsync(obs.Host, obs.Port, obs.Password);
                        }
                    }
                    catch { }
                });
            }
            if (!_twitchService.IsConnected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var twitch = _profileService.GetTwitchSettings();
                        if (twitch.AutoConnect && !string.IsNullOrWhiteSpace(twitch.Channel))
                        {
                            await _twitchService.ConnectAsync(twitch.Channel, twitch.OAuthToken, twitch.BotUsername, twitch.ClientId);
                        }
                    }
                    catch { }
                });
            }
            if (!_kickService.IsConnected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var kick = _profileService.GetKickSettings();
                        if (kick.AutoConnect && !string.IsNullOrWhiteSpace(kick.Channel))
                        {
                            await _kickService.ConnectAsync(kick.Channel, kick.AuthToken, kick.BotUsername);
                        }
                    }
                    catch { }
                });
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.None) continue;

                string? prev = key.LiveValueText;
                UpdateKeyMonitoringValue(key);

                if (key.LiveValueText != prev)
                {
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsSceneChanged(string sceneName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsSceneSwitch)
                {
                    key.IsActiveToggleState = string.Equals(key.ActionParameter, sceneName, StringComparison.OrdinalIgnoreCase);
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsStreamChanged(bool isStreaming)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsToggleStream)
                {
                    key.IsActiveToggleState = isStreaming;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsRecordChanged(bool isRecording)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsToggleRecord)
                {
                    key.IsActiveToggleState = isRecording;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsVirtualCamChanged(bool isActive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsToggleVirtualCam)
                {
                    key.IsActiveToggleState = isActive;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsReplayBufferChanged(bool isActive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsSaveReplayBuffer)
                {
                    key.IsActiveToggleState = isActive;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsStudioModeChanged(bool isActive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsStudioModeToggle)
                {
                    key.IsActiveToggleState = isActive;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsConnectionStateChanged(bool isConnected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (isConnected)
            {
                _ = _obsService.RefreshStreamStatusAsync();
                _ = _obsService.RefreshRecordStatusAsync();
                _ = _obsService.RefreshVirtualCamStatusAsync();
                _ = _obsService.RefreshReplayBufferStatusAsync();
                _ = _obsService.GetStudioModeEnabledAsync();
                _ = _obsService.GetCurrentProgramSceneAsync();
            }
        });
    }

    private async Task ExecuteObsActionAsync(Func<Task> obsAction)
    {
        try
        {
            if (!_obsService.IsConnected)
            {
                var obsCfg = _profileService.GetObsSettings();
                await _obsService.ConnectAsync(obsCfg.Host, obsCfg.Port, obsCfg.Password);
            }

            if (_obsService.IsConnected)
            {
                await obsAction();
            }
        }
        catch { }
    }

    private void OnDiscordVoiceStateChanged(bool isMuted, bool isDeafened)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.DiscordMuteToggle)
                {
                    key.IsActiveToggleState = isMuted;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.DiscordDeafenToggle)
                {
                    key.IsActiveToggleState = isDeafened;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnYouTubeStatusUpdated(YouTubeStreamStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.YouTubeLiveViewers)
                {
                    key.LiveValueText = status.IsLive
                        ? FormatViewerCount(status.ConcurrentViewers)
                        : "OFFLINE";
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private static string FormatViewerCount(int count)
    {
        if (count >= 1_000_000) return $"{count / 1_000_000.0:0.#}M";
        if (count >= 1_000) return $"{count / 1_000.0:0.#}K";
        return count.ToString();
    }

    private void OnTwitchEmoteOnlyChanged(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.TwitchChatEmoteOnly)
                {
                    key.IsActiveToggleState = enabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnTwitchSubOnlyChanged(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.TwitchChatSubOnly)
                {
                    key.IsActiveToggleState = enabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnKickEmoteOnlyChanged(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.KickChatEmoteOnly)
                {
                    key.IsActiveToggleState = enabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnKickSubOnlyChanged(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.KickChatSubOnly)
                {
                    key.IsActiveToggleState = enabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnJetKvmPowerStateChanged(JetKvmPowerState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.JetKvmPowerStatus ||
                    key.AssignedAction == DeckActionType.JetKvmPowerShort)
                {
                    key.IsActiveToggleState = state.Power;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnSpotifyPlaybackStateChanged(SpotifyPlaybackState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.SpotifyToggleShuffle)
                {
                    key.IsActiveToggleState = state.IsShuffle;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.SpotifyToggleRepeat)
                {
                    key.IsActiveToggleState = state.RepeatMode != SpotifyRepeatMode.Off;
                    key.ActionParameter = state.RepeatMode switch
                    {
                        SpotifyRepeatMode.Track => "REP 1",
                        SpotifyRepeatMode.Context => "REP ALL",
                        _ => "REP OFF"
                    };
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.SpotifyLikeTrack)
                {
                    key.IsActiveToggleState = state.IsLiked;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnVlcPlaybackStateChanged(VlcPlaybackState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.VlcPlayPause)
                {
                    key.IsActiveToggleState = state.IsPlaying;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VlcFullscreenToggle)
                {
                    key.IsActiveToggleState = state.IsFullscreen;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnVoicemodStateChanged(VoicemodState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.VoicemodVoiceToggle)
                {
                    key.IsActiveToggleState = state.IsVoiceChangerEnabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VoicemodMuteVoice)
                {
                    key.IsActiveToggleState = state.IsMicMuted;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VoicemodHearMyVoiceToggle)
                {
                    key.IsActiveToggleState = state.IsHearMyVoiceEnabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VoicemodBackgroundToggle)
                {
                    key.IsActiveToggleState = state.IsBackgroundEffectsEnabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VoicemodVoiceSelect)
                {
                    bool isCurrent = !string.IsNullOrWhiteSpace(key.ActionParameter) &&
                                     (string.Equals(key.ActionParameter, state.CurrentVoiceId, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(key.ActionParameter, state.CurrentVoiceName, StringComparison.OrdinalIgnoreCase));
                    key.IsActiveToggleState = isCurrent;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
            SelectedKey?.RefreshVoicemodProperties();
        });
    }

    private void OnVoicemodConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() => SelectedKey?.RefreshVoicemodProperties());
    }

    private void OnVoicemodVoicesLoaded(IReadOnlyList<VoicemodVoiceItem> voices)
    {
        Dispatcher.UIThread.Post(() => SelectedKey?.RefreshVoicemodProperties());
    }

    private void OnVoicemodSoundsLoaded(IReadOnlyList<VoicemodSoundItem> sounds)
    {
        Dispatcher.UIThread.Post(() => SelectedKey?.RefreshVoicemodProperties());
    }

    private void OnKeyStateChanged(int keyIndex, bool isPressed)
    {
        if (keyIndex >= 0 && keyIndex < Keys.Count)
        {
            var key = Keys[keyIndex];
            key.IsPressed = isPressed;

            if (key.AssignedAction == DeckActionType.DiscordPushToTalk)
            {
                _ = _discordService.SetPushToTalkActiveAsync(isPressed);
            }

            if (key.AssignedAction == DeckActionType.StreamDeckPluginAction && !string.IsNullOrEmpty(key.StreamDeckPluginUuid))
            {
                if (isPressed)
                {
                    _ = _streamDeckPluginHost?.SendKeyDownAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson);
                }
                else
                {
                    _ = _streamDeckPluginHost?.SendKeyUpAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson);
                }
            }

            if (key.HasActiveImage || !string.IsNullOrWhiteSpace(key.ActiveCustomTitle) || !string.IsNullOrWhiteSpace(key.ActiveBackgroundColorHex) || key.IsActiveToggleState)
            {
                RenderKeyVisual(key, sendToHardware: true);
            }

            _keyInteractionEngine.HandleKeyEvent(keyIndex, isPressed, key.PressMode);
        }
    }

    private void OnKeyTriggered(int keyIndex, KeyPressMode mode)
    {
        if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                ExecuteKeyAction(Keys[keyIndex]);
            }
        }
        else
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (keyIndex >= 0 && keyIndex < Keys.Count)
                {
                    ExecuteKeyAction(Keys[keyIndex]);
                }
            });
        }
    }

    public void ExecuteKeyAction(KeyItemViewModel key)
    {
        if (key == null || key.AssignedAction == DeckActionType.None) return;

        try
        {
            switch (key.AssignedAction)
            {
                // Multimedia
                case DeckActionType.MediaPlayPause:
                    _systemControl.PlayPause();
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;
                case DeckActionType.MediaNext:
                    _systemControl.NextTrack();
                    break;
                case DeckActionType.MediaPrevious:
                    _systemControl.PreviousTrack();
                    break;
                case DeckActionType.MediaStop:
                    _systemControl.StopTrack();
                    key.IsActiveToggleState = false;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;

                // Audio
                case DeckActionType.VolumeUp:
                    _systemControl.VolumeUp();
                    break;
                case DeckActionType.VolumeDown:
                    _systemControl.VolumeDown();
                    break;
                case DeckActionType.MasterMute:
                    _audioService.ToggleMasterPlaybackMute();
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;
                case DeckActionType.MicMute:
                    _audioService.ToggleMasterRecordingMute();
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;

                // Narzędzia systemowe
                case DeckActionType.Screenshot:
                    _systemControl.TakeScreenshot();
                    break;
                case DeckActionType.TaskManager:
                    _systemControl.OpenTaskManager();
                    break;
                case DeckActionType.Calculator:
                    _systemControl.OpenCalculator();
                    break;
                case DeckActionType.Browser:
                    _systemControl.OpenDefaultBrowser();
                    break;
                case DeckActionType.OpenApplication:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _systemControl.OpenApplication(key.ActionParameter);
                    break;
                case DeckActionType.OpenWebsite:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _systemControl.OpenWebsite(key.ActionParameter);
                    break;
                case DeckActionType.OpenFile:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _systemControl.OpenFile(key.ActionParameter);
                    break;
                case DeckActionType.CloseApplication:
                    _systemControl.CloseApplication(key.ActionParameter);
                    break;
                case DeckActionType.TextPaste:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _systemControl.PasteText(key.ActionParameter);
                    break;
                case DeckActionType.Hotkey:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _systemControl.SendHotkey(key.ActionParameter);
                    break;

                // Zasilanie i blokada
                case DeckActionType.SystemSleep:
                    _systemControl.SystemSleep();
                    break;
                case DeckActionType.SystemShutdown:
                    _systemControl.SystemShutdown();
                    break;
                case DeckActionType.SystemRestart:
                    _systemControl.SystemRestart();
                    break;
                case DeckActionType.SystemLockScreen:
                    _systemControl.SystemLockScreen();
                    break;
                case DeckActionType.SystemScreensaver:
                    _systemControl.SystemScreensaver();
                    break;
                case DeckActionType.SystemVirtualDesktopNext:
                    _systemControl.VirtualDesktopNext();
                    break;
                case DeckActionType.SystemVirtualDesktopPrev:
                    _systemControl.VirtualDesktopPrev();
                    break;
                case DeckActionType.SystemVirtualDesktopCreate:
                    _systemControl.VirtualDesktopCreate();
                    break;
                case DeckActionType.SystemVirtualDesktopClose:
                    _systemControl.VirtualDesktopClose();
                    break;
                case DeckActionType.SystemTaskView:
                    _systemControl.TaskView();
                    break;
                case DeckActionType.SystemShowDesktop:
                    _systemControl.ShowDesktop();
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;
                case DeckActionType.SystemClipboardHistory:
                    _systemControl.OpenClipboardHistory();
                    break;
                case DeckActionType.ConditionalAction:
                    ExecuteConditionalAction(key);
                    break;

                // Razer Hardware
                case DeckActionType.RazerDpiCycle:
                    if (int.TryParse(key.ActionParameter, out int dpi) && dpi > 0)
                        _razerService.SetDpi(dpi);
                    else
                        _razerService.CycleDpiNext();
                    break;
                case DeckActionType.RazerDpiUp:
                    _razerService.CycleDpi(1);
                    break;
                case DeckActionType.RazerDpiDown:
                    _razerService.CycleDpi(-1);
                    break;
                case DeckActionType.RazerDpi400:
                    _razerService.SetDpi(400);
                    break;
                case DeckActionType.RazerDpi800:
                    _razerService.SetDpi(800);
                    break;
                case DeckActionType.RazerDpi1600:
                    _razerService.SetDpi(1600);
                    break;
                case DeckActionType.RazerDpi3200:
                    _razerService.SetDpi(3200);
                    break;
                case DeckActionType.RazerDpi6400:
                    _razerService.SetDpi(6400);
                    break;
                case DeckActionType.RazerPollingRateCycle:
                    if (int.TryParse(key.ActionParameter.Replace("Hz", "").Trim(), out int hz) && hz > 0)
                        _razerService.SetPollingRate(hz);
                    else
                        _razerService.CyclePollingRateNext();
                    break;
                case DeckActionType.RazerPollingRateUp:
                    _razerService.CyclePollingRate(1);
                    break;
                case DeckActionType.RazerPollingRateDown:
                    _razerService.CyclePollingRate(-1);
                    break;
                case DeckActionType.RazerPollingRate125:
                    _razerService.SetPollingRate(125);
                    break;
                case DeckActionType.RazerPollingRate500:
                    _razerService.SetPollingRate(500);
                    break;
                case DeckActionType.RazerPollingRate1000:
                    _razerService.SetPollingRate(1000);
                    break;
                case DeckActionType.RazerPollingRate2000:
                    _razerService.SetPollingRate(2000);
                    break;
                case DeckActionType.RazerPollingRate4000:
                    _razerService.SetPollingRate(4000);
                    break;
                case DeckActionType.RazerPollingRate8000:
                    _razerService.SetPollingRate(8000);
                    break;
                case DeckActionType.RazerProfileNext:
                    _razerService.CycleProfile(1);
                    break;
                case DeckActionType.RazerProfilePrev:
                    _razerService.CycleProfile(-1);
                    break;
                case DeckActionType.RazerProfile1:
                    if (int.TryParse(key.ActionParameter.Replace("Profil", "").Replace("#", "").Trim(), out int prof) && prof >= 1 && prof <= 5)
                        _razerService.SwitchProfile(prof);
                    else
                        _razerService.SwitchProfile(1);
                    break;
                case DeckActionType.RazerProfile2:
                    _razerService.SwitchProfile(2);
                    break;
                case DeckActionType.RazerProfile3:
                    _razerService.SwitchProfile(3);
                    break;
                case DeckActionType.RazerProfile4:
                    _razerService.SwitchProfile(4);
                    break;
                case DeckActionType.RazerProfile5:
                    _razerService.SwitchProfile(5);
                    break;

                // Wooting
                case DeckActionType.WootingProfileCycle:
                case DeckActionType.WootingProfileNext:
                    _wootingService.CycleProfileNext();
                    break;
                case DeckActionType.WootingProfilePrev:
                    _wootingService.CycleProfile(-1);
                    break;
                case DeckActionType.WootingProfileDigital:
                    _wootingService.SwitchProfile(0);
                    break;
                case DeckActionType.WootingProfile1:
                    _wootingService.SwitchProfile(1);
                    break;
                case DeckActionType.WootingProfile2:
                    _wootingService.SwitchProfile(2);
                    break;
                case DeckActionType.WootingProfile3:
                    _wootingService.SwitchProfile(3);
                    break;

                // OBS Studio
                case DeckActionType.ObsToggleStream:
                    if (!_obsService.IsConnected)
                    {
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    _ = ExecuteObsActionAsync(() => _obsService.ToggleStreamAsync());
                    break;
                case DeckActionType.ObsStartStream:
                    _ = ExecuteObsActionAsync(() => _obsService.StartStreamAsync());
                    break;
                case DeckActionType.ObsStopStream:
                    _ = ExecuteObsActionAsync(() => _obsService.StopStreamAsync());
                    break;
                case DeckActionType.ObsToggleRecord:
                    if (!_obsService.IsConnected)
                    {
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    _ = ExecuteObsActionAsync(() => _obsService.ToggleRecordAsync());
                    break;
                case DeckActionType.ObsStartRecord:
                    _ = ExecuteObsActionAsync(() => _obsService.StartRecordAsync());
                    break;
                case DeckActionType.ObsStopRecord:
                    _ = ExecuteObsActionAsync(() => _obsService.StopRecordAsync());
                    break;
                case DeckActionType.ObsToggleVirtualCam:
                    if (!_obsService.IsConnected)
                    {
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    _ = ExecuteObsActionAsync(() => _obsService.ToggleVirtualCamAsync());
                    break;
                case DeckActionType.ObsSaveReplayBuffer:
                    _ = ExecuteObsActionAsync(() => _obsService.SaveReplayBufferAsync());
                    break;
                case DeckActionType.ObsSceneSwitch:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _ = ExecuteObsActionAsync(() => _obsService.SetCurrentProgramSceneAsync(key.ActionParameter));
                    break;
                case DeckActionType.ObsStudioModeToggle:
                    if (!_obsService.IsConnected)
                    {
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    _ = ExecuteObsActionAsync(() => _obsService.ToggleStudioModeAsync());
                    break;
                case DeckActionType.ObsTriggerTransition:
                    _ = ExecuteObsActionAsync(() => _obsService.TriggerStudioModeTransitionAsync());
                    break;

                // Twitch Chat & Helix
                case DeckActionType.TwitchChatEmoteOnly:
                    _ = _twitchService.ToggleEmoteOnlyAsync();
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;
                case DeckActionType.TwitchChatSlowMode:
                    {
                        int secs = int.TryParse(key.ActionParameter, out int s) && s > 0 ? s : 30;
                        _ = _twitchService.ToggleSlowModeAsync(secs);
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    break;
                case DeckActionType.TwitchChatSubOnly:
                    _ = _twitchService.ToggleSubOnlyAsync();
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;
                case DeckActionType.TwitchClearChat:
                    _ = _twitchService.ClearChatAsync();
                    break;
                case DeckActionType.TwitchSendMessage:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _ = _twitchService.SendChatMessageAsync(key.ActionParameter);
                    break;
                case DeckActionType.TwitchCreateClip:
                    _ = _twitchService.CreateClipAsync();
                    break;
                case DeckActionType.TwitchCreateMarker:
                    _ = _twitchService.CreateStreamMarkerAsync(key.ActionParameter);
                    break;
                case DeckActionType.TwitchStartRaid:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _ = _twitchService.StartRaidAsync(key.ActionParameter);
                    break;
                case DeckActionType.TwitchCancelRaid:
                    _ = _twitchService.CancelRaidAsync();
                    break;
                case DeckActionType.TwitchRunCommercial:
                    {
                        int commSecs = int.TryParse(key.ActionParameter, out int cs) && cs > 0 ? cs : 30;
                        _ = _twitchService.StartCommercialAsync(commSecs);
                    }
                    break;

                // Kick Chat & Actions
                case DeckActionType.KickChatEmoteOnly:
                    _ = _kickService.ToggleEmoteOnlyAsync();
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;
                case DeckActionType.KickChatSlowMode:
                    {
                        int secs = int.TryParse(key.ActionParameter, out int s) && s > 0 ? s : 30;
                        _ = _kickService.ToggleSlowModeAsync(secs);
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    break;
                case DeckActionType.KickChatSubOnly:
                    _ = _kickService.ToggleSubOnlyAsync();
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                    break;
                case DeckActionType.KickClearChat:
                    _ = _kickService.ClearChatAsync();
                    break;
                case DeckActionType.KickSendMessage:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _ = _kickService.SendChatMessageAsync(key.ActionParameter);
                    break;
                case DeckActionType.KickCreateClip:
                    _ = _kickService.CreateClipAsync();
                    break;

                // JetKVM ATX Remote Power Control
                case DeckActionType.JetKvmPowerShort:
                    _ = _jetKvmService.PowerShortPressAsync();
                    break;
                case DeckActionType.JetKvmPowerLong:
                    _ = _jetKvmService.PowerLongPressAsync();
                    break;
                case DeckActionType.JetKvmReset:
                    _ = _jetKvmService.ResetAsync();
                    break;
                case DeckActionType.JetKvmPowerStatus:
                    _ = _jetKvmService.RefreshStateAsync();
                    break;

                // Spotify Extended Playback & Playlists
                case DeckActionType.SpotifyToggleShuffle:
                    _ = _spotifyService.ToggleShuffleAsync();
                    break;
                case DeckActionType.SpotifyToggleRepeat:
                    _ = _spotifyService.ToggleRepeatAsync();
                    break;
                case DeckActionType.SpotifyLikeTrack:
                    _ = _spotifyService.ToggleLikeTrackAsync();
                    break;
                case DeckActionType.SpotifyAddToQueue:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    {
                        _ = _spotifyService.AddToQueueAsync(key.ActionParameter);
                    }
                    break;
                case DeckActionType.SpotifyPlayUri:
                    _ = _spotifyService.PlayUriAsync(key.ActionParameter);
                    break;

                // VLC Media Player
                case DeckActionType.VlcPlayPause:
                    _ = _vlcService.PlayPauseAsync();
                    break;
                case DeckActionType.VlcStop:
                    _ = _vlcService.StopAsync();
                    break;
                case DeckActionType.VlcNext:
                    _ = _vlcService.NextTrackAsync();
                    break;
                case DeckActionType.VlcPrevious:
                    _ = _vlcService.PreviousTrackAsync();
                    break;
                case DeckActionType.VlcFullscreenToggle:
                    _ = _vlcService.ToggleFullscreenAsync();
                    break;

                // Voicemod
                case DeckActionType.VoicemodVoiceSelect:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    {
                        _ = _voicemodService.SelectVoiceAsync(key.ActionParameter);
                    }
                    break;
                case DeckActionType.VoicemodVoiceToggle:
                    _ = _voicemodService.ToggleVoiceChangerAsync();
                    break;
                case DeckActionType.VoicemodMuteVoice:
                    _ = _voicemodService.ToggleMuteMicAsync();
                    break;
                case DeckActionType.VoicemodHearMyVoiceToggle:
                    _ = _voicemodService.ToggleHearMyVoiceAsync();
                    break;
                case DeckActionType.VoicemodBackgroundToggle:
                    _ = _voicemodService.ToggleBackgroundEffectsAsync();
                    break;
                case DeckActionType.VoicemodPlaySound:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    {
                        _ = _voicemodService.PlaySoundAsync(key.ActionParameter);
                    }
                    break;
                case DeckActionType.VoicemodStopAllSounds:
                    _ = _voicemodService.StopAllSoundsAsync();
                    break;

                // Streaming (YouTube & Streamlabs)
                case DeckActionType.YouTubeLiveViewers:
                    _ = _youtubeService.FetchStreamStatusAsync(key.ActionParameter);
                    break;
                case DeckActionType.StreamlabsSceneSwitch:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                        _ = _streamlabsService.SwitchSceneAsync(key.ActionParameter);
                    break;

                // Discord
                case DeckActionType.DiscordMuteToggle:
                    var dcMuteCfg = _profileService.GetDiscordSettings();
                    if (dcMuteCfg.UseRpc && _discordService.IsConnected && _discordService.IsAuthenticated)
                    {
                        _ = _discordService.ToggleMuteAsync();
                    }
                    else
                    {
                        string hotkey = !string.IsNullOrWhiteSpace(dcMuteCfg.MuteHotkey) ? dcMuteCfg.MuteHotkey : "Ctrl+Shift+M";
                        _systemControl.SendHotkey(hotkey);
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    break;
                case DeckActionType.DiscordDeafenToggle:
                    var dcDeafenCfg = _profileService.GetDiscordSettings();
                    if (dcDeafenCfg.UseRpc && _discordService.IsConnected && _discordService.IsAuthenticated)
                    {
                        _ = _discordService.ToggleDeafenAsync();
                    }
                    else
                    {
                        string hotkey = !string.IsNullOrWhiteSpace(dcDeafenCfg.DeafenHotkey) ? dcDeafenCfg.DeafenHotkey : "Ctrl+Shift+D";
                        _systemControl.SendHotkey(hotkey);
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    break;
                case DeckActionType.DiscordCameraToggle:
                    if (_discordService.IsConnected && _discordService.IsAuthenticated)
                    {
                        _ = _discordService.ToggleCameraAsync();
                    }
                    else
                    {
                        _systemControl.SendHotkey("Ctrl+Shift+O");
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    break;
                case DeckActionType.DiscordScreenShareToggle:
                    if (_discordService.IsConnected && _discordService.IsAuthenticated)
                    {
                        _ = _discordService.ToggleScreenShareAsync();
                    }
                    else
                    {
                        _systemControl.SendHotkey("Ctrl+Shift+S");
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    break;
                case DeckActionType.DiscordPushToTalk:
                    // Obsługiwane bezpośrednio w OnKeyStateChanged (press / release)
                    break;
                case DeckActionType.DiscordJoinVoiceChannel:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    {
                        var (guildId, channelId) = DiscordService.ExtractGuildAndChannelId(key.ActionParameter);
                        string targetChannel = channelId ?? key.ActionParameter.Trim();
                        string targetGuild = !string.IsNullOrWhiteSpace(guildId) ? guildId : "@me";

                        if (_discordService.IsConnected && _discordService.IsAuthenticated)
                        {
                            _ = _discordService.SelectVoiceChannelAsync(targetChannel);
                        }

                        // Natywny protokół URI discord:// natychmiast przenosi i dołącza do kanału w aplikacji Discord Desktop
                        try
                        {
                            Process.Start(new ProcessStartInfo($"discord://-/channels/{targetGuild}/{targetChannel}") { UseShellExecute = true });
                        }
                        catch { }
                    }
                    break;

                // MS Teams
                case DeckActionType.TeamsMuteToggle:
                    _ = _teamsService.ToggleMuteAsync();
                    break;
                case DeckActionType.TeamsCameraToggle:
                    _ = _teamsService.ToggleCameraAsync();
                    break;
                case DeckActionType.TeamsRaiseHand:
                    _ = _teamsService.ToggleRaiseHandAsync();
                    break;
                case DeckActionType.TeamsLeaveMeeting:
                    _ = _teamsService.LeaveMeetingAsync();
                    break;

                // Nawigacja & Profile
                case DeckActionType.GoToProfile:
                    key.IsPressed = false;
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    {
                        var targetProf = _profileService.GetAllProfiles().FirstOrDefault(p =>
                            p.Id.Equals(key.ActionParameter, StringComparison.OrdinalIgnoreCase) ||
                            p.Name.Equals(key.ActionParameter, StringComparison.OrdinalIgnoreCase));
                        if (targetProf != null)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                DeselectAll();
                                _profileService.SetActiveProfile(targetProf.Id);
                                ApplyProfile(targetProf);
                            });
                        }
                    }
                    break;
                case DeckActionType.GoToPage:
                    key.IsPressed = false;
                    {
                        int targetPage = ParsePageIndex(key.ActionParameter);
                        Dispatcher.UIThread.Post(() =>
                        {
                            DeselectAll();
                            SwitchToPage(targetPage);
                        });
                    }
                    break;
                case DeckActionType.OpenFolder:
                    key.IsPressed = false;
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            DeselectAll();
                            OpenFolder(key.ActionParameter);
                        });
                    }
                    break;
                case DeckActionType.GoBack:
                    key.IsPressed = false;
                    Dispatcher.UIThread.Post(() =>
                    {
                        DeselectAll();
                        NavigateBack();
                    });
                    break;
                case DeckActionType.GoHome:
                    key.IsPressed = false;
                    Dispatcher.UIThread.Post(() =>
                    {
                        DeselectAll();
                        NavigateHome();
                    });
                    break;
                case DeckActionType.NextPage:
                    key.IsPressed = false;
                    Dispatcher.UIThread.Post(() =>
                    {
                        DeselectAll();
                        NextPage();
                    });
                    break;
                case DeckActionType.PreviousPage:
                    key.IsPressed = false;
                    Dispatcher.UIThread.Post(() =>
                    {
                        DeselectAll();
                        PreviousPage();
                    });
                    break;
                case DeckActionType.HotkeySwitch:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    {
                        var parts = key.ActionParameter.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                        {
                            key.IsActiveToggleState = !key.IsActiveToggleState;
                            string hk = key.IsActiveToggleState ? parts[1] : parts[0];
                            _systemControl.SendHotkey(hk);
                            RenderKeyVisual(key, sendToHardware: true);
                        }
                        else
                        {
                            _systemControl.SendHotkey(key.ActionParameter);
                        }
                    }
                    break;

                // Akcje monitora sprzętowego – intencjonalnie brak uruchamiania Menedżera Zadań:
                // Zgodnie z wymaganiem: "tak samo jak są kacje monitora to nei chce by defaultowo otwierało manager zadan"
                case DeckActionType.DisplayCpuUsage:
                case DeckActionType.DisplayRamUsage:
                case DeckActionType.DisplayGpuUsage:
                case DeckActionType.DisplayCpuTemp:
                case DeckActionType.DisplayGpuTemp:
                case DeckActionType.DisplayNetworkSpeed:
                case DeckActionType.DisplayDiskUsage:
                case DeckActionType.DisplayClock:
                case DeckActionType.DisplayDate:
                case DeckActionType.DisplayFps:
                case DeckActionType.NowPlaying:
                    break;

                // Smart Home: Home Assistant
                case DeckActionType.HomeAssistantToggle:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter) && _homeAssistantService != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            bool ok = await _homeAssistantService.ToggleAsync(key.ActionParameter);
                            var entity = _homeAssistantService.GetEntity(key.ActionParameter);
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (entity != null)
                                {
                                    key.IsActiveToggleState = entity.IsOn;
                                }
                                else
                                {
                                    key.IsActiveToggleState = !key.IsActiveToggleState;
                                }
                                RenderKeyVisual(key, sendToHardware: true);
                            });
                        });
                    }
                    break;
                case DeckActionType.HomeAssistantTurnOn:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter) && _homeAssistantService != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            await _homeAssistantService.TurnOnAsync(key.ActionParameter);
                            Dispatcher.UIThread.Post(() =>
                            {
                                key.IsActiveToggleState = true;
                                RenderKeyVisual(key, sendToHardware: true);
                            });
                        });
                    }
                    break;
                case DeckActionType.HomeAssistantTurnOff:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter) && _homeAssistantService != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            await _homeAssistantService.TurnOffAsync(key.ActionParameter);
                            Dispatcher.UIThread.Post(() =>
                            {
                                key.IsActiveToggleState = false;
                                RenderKeyVisual(key, sendToHardware: true);
                            });
                        });
                    }
                    break;
                case DeckActionType.HomeAssistantCallService:
                    if (!string.IsNullOrWhiteSpace(key.ActionParameter) && _homeAssistantService != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            var parts = key.ActionParameter.Split('.', 2);
                            if (parts.Length == 2)
                            {
                                await _homeAssistantService.CallServiceAsync(parts[0], parts[1]);
                            }
                        });
                    }
                    break;

                case DeckActionType.StreamDeckPluginAction:
                    if (!string.IsNullOrEmpty(key.StreamDeckPluginUuid) && _streamDeckPluginHost != null)
                    {
                        _ = _streamDeckPluginHost.SendKeyDownAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson);
                        _ = Task.Delay(50).ContinueWith(_ => _streamDeckPluginHost.SendKeyUpAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error executing action {key.AssignedAction}: {ex.Message}");
        }
    }

    private void ExecuteConditionalAction(KeyItemViewModel key)
    {
        if (key == null) return;
        var model = ConditionalActionModel.Parse(key.ActionParameter);
        bool conditionMet = model.Evaluate(key.IsActiveToggleState);
        DeckActionType targetAction = conditionMet ? model.IfTrueAction : model.IfFalseAction;
        string targetParam = conditionMet ? model.IfTrueParameter : model.IfFalseParameter;

        if (targetAction == DeckActionType.None || targetAction == DeckActionType.ConditionalAction)
        {
            return;
        }

        var tempKey = new KeyItemViewModel
        {
            Index = key.Index,
            AssignedAction = targetAction,
            ActionParameter = targetParam,
            IsActiveToggleState = key.IsActiveToggleState
        };

        ExecuteKeyAction(tempKey);
        if (tempKey.IsActiveToggleState != key.IsActiveToggleState)
        {
            key.IsActiveToggleState = tempKey.IsActiveToggleState;
            RenderKeyVisual(key, sendToHardware: true);
        }
    }

    private void OnSliderValueChanged(int sliderIndex, byte raw, int percent)
    {
        if (sliderIndex < 0 || sliderIndex >= Sliders.Count) return;

        var slider = Sliders[sliderIndex];
        int prevPercent = slider.Percent;
        slider.RawValue = raw;
        slider.Percent = percent;

        // Absolutnie NIE dotykaj audio Windows podczas startu programu i inicjalizacji urządzeń
        if (_suppressAudioOnStartup) return;

        // Jeśli suwak nie ma przypisanego celu audio, nie rób nic
        if (slider.TargetType == AudioTargetType.None) return;

        // Ogranicz drobne drgania (jitter)
        if (Math.Abs(percent - prevPercent) < 1) return;

        try
        {
            float vol = Math.Clamp(percent / 100.0f, 0.0f, 1.0f);
            switch (slider.TargetType)
            {
                case AudioTargetType.MasterPlayback:
                    _audioService.SetMasterPlaybackVolume(vol);
                    break;
                case AudioTargetType.MasterRecording:
                    _audioService.SetMasterRecordingVolume(vol);
                    break;
                case AudioTargetType.Process:
                    if (!string.IsNullOrWhiteSpace(slider.TargetProcessName))
                    {
                        _audioService.SetProcessVolume(slider.TargetProcessName, vol);
                    }
                    break;
                case AudioTargetType.HomeAssistantLight:
                    if (!string.IsNullOrWhiteSpace(slider.TargetProcessName) && _homeAssistantService != null)
                    {
                        byte brightness = (byte)Math.Clamp((int)Math.Round((percent / 100.0) * 255.0), 0, 255);
                        _ = _homeAssistantService.SetLightBrightnessAsync(slider.TargetProcessName, brightness);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CanvasViewModel] Błąd regulacji głośności: {ex.Message}");
        }
    }

    private void ExecuteRollerAction(RollerActionType action, int direction)
    {
        switch (action)
        {
            case RollerActionType.MasterVolume:
                int steps = Math.Abs(direction);
                for (int s = 0; s < steps; s++)
                {
                    if (direction > 0) _systemControl.VolumeUp();
                    else _systemControl.VolumeDown();
                }
                break;
            case RollerActionType.ScrollVertical:
                _systemControl.ScrollVertical(direction * 3);
                break;
            case RollerActionType.ScrollHorizontal:
                _systemControl.ScrollHorizontal(direction * 3);
                break;
            case RollerActionType.MediaTrack:
                if (direction > 0) _systemControl.NextTrack();
                else _systemControl.PreviousTrack();
                break;
            case RollerActionType.RazerDpiCycle:
                _razerService.CycleDpi(direction);
                break;
            case RollerActionType.RazerProfileCycle:
                _razerService.CycleProfile(direction);
                break;
            case RollerActionType.RazerPollingRateCycle:
                _razerService.CyclePollingRate(direction);
                break;
            case RollerActionType.WootingProfileCycle:
                if (direction > 0) _wootingService.CycleProfileNext();
                else _wootingService.CycleProfile(-1);
                break;
            case RollerActionType.HomeAssistantLightBrightness:
                if (_homeAssistantService != null)
                {
                    _ = _homeAssistantService.AdjustLightBrightnessAsync(null, direction * 15);
                }
                break;
            case RollerActionType.None:
            default:
                break;
        }
    }

    private void OnRollerRotated(int rollerIndex, int direction)
    {
        if (rollerIndex >= 0 && rollerIndex < Rollers.Count)
        {
            var roller = Rollers[rollerIndex];
            roller.Steps += direction;
            roller.LastDirection = direction > 0 ? "Góra ▲" : "Dół ▼";
            ExecuteRollerAction(roller.AssignedAction, direction);
        }
    }

    private void OnKnobRotated(int knobIndex, int delta)
    {
        if (knobIndex >= 0 && knobIndex < Knobs.Count)
        {
            var knob = Knobs[knobIndex];
            knob.Steps += delta;
            knob.LastDirection = delta > 0 ? "CW ▶" : "CCW ◀";
            knob.IsActivityActive = true;
            ExecuteRollerAction(knob.RotateAction, delta);
        }
    }

    private void OnKnobPressed(int knobIndex, bool isPressed)
    {
        if (knobIndex >= 0 && knobIndex < Knobs.Count)
        {
            var knob = Knobs[knobIndex];
            knob.IsPressed = isPressed;
            if (isPressed && knob.PressAction != DeckActionType.None)
            {
                ExecuteKeyAction(new KeyItemViewModel
                {
                    Index = -1,
                    AssignedAction = knob.PressAction,
                    ActionParameter = knob.PressActionParameter
                });
            }
        }
    }

    private void OnDialRotated(int delta)
    {
        if (Dial != null)
        {
            Dial.Steps += delta;
            Dial.LastDirection = delta > 0 ? "CW ▶" : "CCW ◀";
            Dial.IsActivityActive = true;
            ExecuteRollerAction(Dial.RotateAction, delta);
        }
    }

    private void OnDialPressed(bool isPressed)
    {
        if (Dial != null)
        {
            Dial.IsPressed = isPressed;
            if (isPressed && Dial.PressAction != DeckActionType.None)
            {
                ExecuteKeyAction(new KeyItemViewModel
                {
                    Index = -1,
                    AssignedAction = Dial.PressAction,
                    ActionParameter = Dial.PressActionParameter
                });
            }
        }
    }

    private void OnKeyRenderedByService(int keyIndex, byte[] jpeg)
    {
        if (_animatedKeyRenderer.IsAnimating(keyIndex)) return;

        int version = _pageRenderVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _pageRenderVersion) return;
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                Keys[keyIndex].SetRenderedJpeg(jpeg);
            }
        });
    }

    private void OnAnimatedFrameRendered(int keyIndex, int frameIndex, byte[] jpeg)
    {
        if (_isUiSuspended) return;

        int version = _pageRenderVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isUiSuspended) return;
            if (version != _pageRenderVersion) return;
            if (!_animatedKeyRenderer.IsAnimating(keyIndex)) return;
            if (keyIndex < 0 || keyIndex >= Keys.Count) return;

            var key = Keys[keyIndex];
            bool isStateActive = (key.IsActiveToggleState || key.IsPressed);
            string effectiveMedia = (isStateActive && key.HasActiveImage)
                ? key.ActiveImagePath
                : key.ImagePath;

            if (!MediaValidationService.IsAnimatedOrVideo(effectiveMedia)) return;

            key.SetRenderedJpeg(jpeg);
        });
    }

    private void OnKeyChanged(KeyItemViewModel key)
    {
        UpdateKeyMonitoringValue(key);
        RenderKeyVisual(key, sendToHardware: true);
        ScheduleAutoSave();
    }

    public void RenderKeyVisual(KeyItemViewModel key, bool sendToHardware)
    {
        try
        {
            bool isStateActive = (key.IsActiveToggleState || key.IsPressed);
            string effectiveMedia = (isStateActive && key.HasActiveImage)
                ? key.ActiveImagePath
                : key.ImagePath;

            string effectiveFit = (isStateActive && key.HasActiveImage)
                ? key.ActiveImageFitMode
                : key.ImageFitMode;

            int effectiveScale = (isStateActive && key.HasActiveImage)
                ? key.ActiveImageScale
                : key.ImageScale;

            int effectiveOffX = (isStateActive && key.HasActiveImage)
                ? key.ActiveImageOffsetX
                : key.ImageOffsetX;

            int effectiveOffY = (isStateActive && key.HasActiveImage)
                ? key.ActiveImageOffsetY
                : key.ImageOffsetY;

            if (MediaValidationService.IsAnimatedOrVideo(effectiveMedia))
            {
                _animatedKeyRenderer.PlayMedia(key.Index, effectiveMedia, 10, effectiveFit, effectiveScale, effectiveOffX, effectiveOffY);
                return;
            }

            _animatedKeyRenderer.StopAnimation(key.Index);

            string? effectiveTitle = key.HasAction
                ? ((isStateActive && !string.IsNullOrWhiteSpace(key.ActiveCustomTitle))
                    ? key.ActiveCustomTitle
                    : (!string.IsNullOrWhiteSpace(key.CustomTitle) ? key.CustomTitle : key.DisplayTitle))
                : (!string.IsNullOrWhiteSpace(key.CustomTitle) ? key.CustomTitle : "");

            string? effectiveSubtitle = key.GetEffectiveSubtitle();
            if (key.HasAction && string.IsNullOrWhiteSpace(effectiveSubtitle) && !string.IsNullOrWhiteSpace(key.DisplaySubtitle))
            {
                effectiveSubtitle = key.DisplaySubtitle;
            }
            else if (!key.HasAction && string.IsNullOrWhiteSpace(effectiveSubtitle))
            {
                effectiveSubtitle = "";
            }

            byte[] jpeg;
            if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
            {
                jpeg = ButtonImageRenderer.RenderStreamDeckKeyJpeg(
                    pluginImageJpeg: key.PluginDynamicImageBytes,
                    title: effectiveTitle,
                    fallbackImagePath: key.ImagePath,
                    isPressed: key.IsPressed,
                    fontFamily: key.FontFamily,
                    fontSize: key.FontSize,
                    isBold: key.IsBold,
                    isItalic: key.IsItalic,
                    textColorHex: key.TextColorHex,
                    isAlert: key.IsStreamDeckAlert,
                    isOk: key.IsStreamDeckOk);
            }
            else if (IsPanoramicWallpaperActive && !string.IsNullOrWhiteSpace(PanoramicWallpaperPath) &&
                System.IO.File.Exists(PanoramicWallpaperPath) && !key.HasAction && !key.HasCustomImage)
            {
                byte[] panoJpeg = ButtonImageRenderer.RenderPanoramicImage(PanoramicWallpaperPath, PanoramicWallpaperFitMode);
                jpeg = ButtonImageRenderer.SlicePanoramicKeyJpeg(panoJpeg, key.Index);
            }
            else
            {
                jpeg = ButtonImageRenderer.RenderCustomKeyJpeg(
                    key.AssignedAction,
                    key.Number,
                    effectiveTitle,
                    effectiveSubtitle,
                    key.BackgroundColorHex,
                    key.TextColorHex,
                    key.ImagePath,
                    key.FontFamily,
                    key.FontSize,
                    key.IsPressed,
                    key.ActiveImagePath,
                    key.IsActiveToggleState,
                    key.SubtitleColorHex,
                    key.IsBold,
                    key.IsItalic,
                    key.IsStrikeThrough,
                    key.ActiveCustomTitle,
                    key.ActiveCustomSubtitle,
                    key.ActiveBackgroundColorHex,
                    key.ActiveTextColorHex,
                    key.ActiveSubtitleColorHex,
                    key.ImageFitMode,
                    key.ImageScale,
                    key.ImageOffsetX,
                    key.ImageOffsetY,
                    key.ActiveImageFitMode,
                    key.ActiveImageScale,
                    key.ActiveImageOffsetX,
                    key.ActiveImageOffsetY);
            }

            if (!_isUiSuspended)
            {
                key.SetRenderedJpeg(jpeg);
            }

            if (sendToHardware)
            {
                _deviceService.SetKeyImage(key.Index, jpeg);
            }
        }
        catch
        {
            // Bezpieczne tłumienie wyjątków renderowania przy nieprawidłowych parametrach
        }
    }

    [RelayCommand]
    public void SelectKey(KeyItemViewModel key)
    {
        ClearSelection();
        key.IsSelected = true;
        SelectedElement = key;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectSlider(SliderItemViewModel slider)
    {
        ClearSelection();
        slider.IsSelected = true;
        SelectedElement = slider;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectRoller(RollerItemViewModel roller)
    {
        ClearSelection();
        roller.IsSelected = true;
        SelectedElement = roller;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectBase()
    {
        ClearSelection();
        BaseStation.IsSelected = true;
        SelectedElement = BaseStation;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectKnob(KnobItemViewModel knob)
    {
        ClearSelection();
        knob.IsSelected = true;
        SelectedElement = knob;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectDial()
    {
        if (Dial == null) return;
        ClearSelection();
        Dial.IsSelected = true;
        SelectedElement = Dial;
        NotifySelectionChanged();
    }

    private void ClearSelection()
    {
        foreach (var k in Keys)
        {
            k.IsSelected = false;
            k.IsPressed = false;
        }
        foreach (var s in Sliders) s.IsSelected = false;
        foreach (var r in Rollers) r.IsSelected = false;
        foreach (var kn in Knobs) kn.IsSelected = false;
        if (Dial != null) Dial.IsSelected = false;
        BaseStation.IsSelected = false;
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(IsKeySelected));
        OnPropertyChanged(nameof(IsSliderSelected));
        OnPropertyChanged(nameof(IsRollerSelected));
        OnPropertyChanged(nameof(IsKnobSelected));
        OnPropertyChanged(nameof(IsDialSelected));
        OnPropertyChanged(nameof(IsBaseSelected));
        OnPropertyChanged(nameof(SelectedKey));
        OnPropertyChanged(nameof(SelectedSlider));
        OnPropertyChanged(nameof(SelectedRoller));
        OnPropertyChanged(nameof(SelectedKnob));
        OnPropertyChanged(nameof(SelectedDial));
        OnPropertyChanged(nameof(SelectedBase));
    }

    public void SaveCurrentStateToActiveProfile()
    {
        try
        {
            var profile = _profileService.GetActiveProfile();
            if (profile == null) return;

            EnsurePagesInitialized(profile);
            profile.Folders ??= new List<KeyFolderModel>();

            // Zapisz klawisze do bieżącego widoku (folder lub strona)
            var currentKeyAssignments = new List<KeyAssignmentModel>();
            foreach (var key in Keys)
            {
                currentKeyAssignments.Add(new KeyAssignmentModel
                {
                    Index = key.Index,
                    Action = key.AssignedAction,
                    PressMode = key.PressMode,
                    CustomTitle = key.CustomTitle,
                    CustomSubtitle = key.CustomSubtitle,
                    ActiveCustomTitle = key.ActiveCustomTitle,
                    ActiveCustomSubtitle = key.ActiveCustomSubtitle,
                    BackgroundColorHex = key.BackgroundColorHex,
                    TextColorHex = key.TextColorHex,
                    SubtitleColorHex = key.SubtitleColorHex,
                    ActiveBackgroundColorHex = key.ActiveBackgroundColorHex,
                    ActiveTextColorHex = key.ActiveTextColorHex,
                    ActiveSubtitleColorHex = key.ActiveSubtitleColorHex,
                    ImagePath = key.ImagePath,
                    ActiveImagePath = key.ActiveImagePath,
                    FontFamily = key.FontFamily,
                    FontSize = key.FontSize,
                    IsBold = key.IsBold,
                    IsItalic = key.IsItalic,
                    IsStrikeThrough = key.IsStrikeThrough,
                    ActionParameter = key.ActionParameter,
                    ConditionalAction = key.AssignedAction == DeckActionType.ConditionalAction ? ConditionalActionModel.Parse(key.ActionParameter) : null,
                    StateMode = key.StateMode,
                    ObsSceneName = key.ObsSceneName,
                    ImageFitMode = key.ImageFitMode,
                    ImageScale = key.ImageScale,
                    ImageOffsetX = key.ImageOffsetX,
                    ImageOffsetY = key.ImageOffsetY,
                    ActiveImageFitMode = key.ActiveImageFitMode,
                    ActiveImageScale = key.ActiveImageScale,
                    ActiveImageOffsetX = key.ActiveImageOffsetX,
                    ActiveImageOffsetY = key.ActiveImageOffsetY,
                    StreamDeckPluginUuid = key.StreamDeckPluginUuid,
                    StreamDeckActionUuid = key.StreamDeckActionUuid,
                    StreamDeckSettingsJson = key.StreamDeckSettingsJson
                });
            }

            if (IsInsideFolder && CurrentFolderId != null)
            {
                var folder = profile.Folders.FirstOrDefault(f => f.Id == CurrentFolderId);
                if (folder != null)
                {
                    folder.Keys = currentKeyAssignments;
                }
            }
            else
            {
                if (CurrentPageIndex >= 0 && CurrentPageIndex < profile.Pages.Count)
                {
                    profile.Pages[CurrentPageIndex].Keys = currentKeyAssignments;
                    profile.Pages[CurrentPageIndex].WallpaperMode = WallpaperMode;
                    profile.Pages[CurrentPageIndex].PanoramicWallpaperPath = PanoramicWallpaperPath;
                    profile.Pages[CurrentPageIndex].PanoramicWallpaperFitMode = PanoramicWallpaperFitMode;
                }
                if (CurrentPageIndex == 0)
                {
                    profile.Keys = new List<KeyAssignmentModel>(currentKeyAssignments);
                    profile.WallpaperMode = WallpaperMode;
                    profile.PanoramicWallpaperPath = PanoramicWallpaperPath;
                    profile.PanoramicWallpaperFitMode = PanoramicWallpaperFitMode;
                }
            }

            // Zapisz suwaki
            profile.Sliders.Clear();
            foreach (var slider in Sliders)
            {
                profile.Sliders.Add(new SliderAssignmentModel
                {
                    Index = slider.Index,
                    TargetType = slider.TargetType,
                    TargetProcessName = slider.TargetProcessName,
                    DisplayTarget = slider.SelectedTarget,
                    ColorHex = slider.ColorHex,
                    Brightness = slider.Brightness
                });
            }

            // Zapisz rolki
            profile.Rollers.Clear();
            foreach (var roller in Rollers)
            {
                profile.Rollers.Add(new RollerAssignmentModel
                {
                    Index = roller.Index,
                    Action = roller.AssignedAction,
                    ColorHex = roller.ColorHex,
                    Brightness = roller.Brightness
                });
            }

            // Zapisz pokrętła 3 Knobs
            profile.Knobs ??= new List<KnobAssignmentModel>();
            profile.Knobs.Clear();
            foreach (var knob in Knobs)
            {
                profile.Knobs.Add(new KnobAssignmentModel
                {
                    Index = knob.Index,
                    RotateAction = knob.RotateAction,
                    PressAction = knob.PressAction,
                    PressActionParameter = knob.PressActionParameter,
                    LedMode = knob.LedMode,
                    ColorHex = knob.ColorHex,
                    Brightness = knob.Brightness,
                    Speed = knob.Speed
                });
            }

            // Zapisz Dial IPS
            if (Dial != null)
            {
                profile.Dial = new DialAssignmentModel
                {
                    RotateAction = Dial.RotateAction,
                    PressAction = Dial.PressAction,
                    PressActionParameter = Dial.PressActionParameter,
                    Brightness = Dial.Brightness,
                    ColorHex = Dial.ColorHex,
                    ImagePath = Dial.ImagePath,
                    DisplayWidget = Dial.DisplayWidget
                };
            }

            // Zapisz stację dokującą i podświetlenie
            profile.LedColorHex = BaseStation.ColorHex;
            profile.BaseBrightness = BaseStation.Brightness;
            profile.KeypadBrightness = KeypadBrightness;

            // Zapisz rotację i orientację paneli
            profile.RotationMode = RotationMode;
            profile.ManualRotationAngle = ManualRotationAngle;

            _profileService.SaveProfile(profile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to auto-save profile: {ex.Message}");
        }
    }

    public void SyncPanoramicWallpaperStateToKeys()
    {
        bool isActive = IsPanoramicWallpaperActive;
        string? path = PanoramicWallpaperPath;
        string fitMode = PanoramicWallpaperFitMode;
        foreach (var k in Keys)
        {
            k.IsPanoramicWallpaperActive = isActive;
            k.PanoramicWallpaperPath = path;
            k.PanoramicWallpaperFitMode = fitMode;
        }
    }

    public void ApplyPanoramicWallpaper(string path, string fitMode = "Cover")
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;

        try
        {
            WallpaperMode = DeckWallpaperMode.PanoramicDeck;
            PanoramicWallpaperPath = path;
            PanoramicWallpaperFitMode = fitMode;

            var profile = _profileService.GetActiveProfile();
            if (profile != null)
            {
                profile.WallpaperMode = DeckWallpaperMode.PanoramicDeck;
                profile.PanoramicWallpaperPath = path;
                profile.PanoramicWallpaperFitMode = fitMode;

                EnsurePagesInitialized(profile);
                if (CurrentPageIndex >= 0 && CurrentPageIndex < profile.Pages.Count)
                {
                    var page = profile.Pages[CurrentPageIndex];
                    page.WallpaperMode = DeckWallpaperMode.PanoramicDeck;
                    page.PanoramicWallpaperPath = path;
                    page.PanoramicWallpaperFitMode = fitMode;
                }
                _profileService.SaveProfile(profile);
            }

            SyncPanoramicWallpaperStateToKeys();

            byte[] panoJpeg = ButtonImageRenderer.RenderPanoramicImage(path, fitMode);
            _deviceService.SetPanoramicWallpaper(panoJpeg);

            for (int i = 0; i < Keys.Count && i < 15; i++)
            {
                byte[] keySlice = ButtonImageRenderer.SlicePanoramicKeyJpeg(panoJpeg, i);
                Keys[i].SetRenderedJpeg(keySlice);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Błąd nakładania tapety panoramicznej: {ex.Message}");
        }
    }

    public void ClearPanoramicWallpaper()
    {
        WallpaperMode = DeckWallpaperMode.PerKey;
        PanoramicWallpaperPath = null;

        var profile = _profileService.GetActiveProfile();
        if (profile != null)
        {
            profile.WallpaperMode = DeckWallpaperMode.PerKey;
            profile.PanoramicWallpaperPath = null;

            if (CurrentPageIndex >= 0 && CurrentPageIndex < profile.Pages.Count)
            {
                var page = profile.Pages[CurrentPageIndex];
                page.WallpaperMode = DeckWallpaperMode.PerKey;
                page.PanoramicWallpaperPath = null;
            }
            _profileService.SaveProfile(profile);
        }

        SyncPanoramicWallpaperStateToKeys();

        for (int i = 0; i < Keys.Count; i++)
        {
            RenderKeyVisual(Keys[i], sendToHardware: true);
        }
    }

    [RelayCommand]
    public void CycleRotation()
    {
        RotationMode = RotationMode switch
        {
            DeckRotationMode.Auto => DeckRotationMode.Manual90,
            DeckRotationMode.Manual90 => DeckRotationMode.Manual180,
            DeckRotationMode.Manual180 => DeckRotationMode.Manual270,
            DeckRotationMode.Manual270 => DeckRotationMode.Manual0,
            _ => DeckRotationMode.Auto
        };

        ManualRotationAngle = RotationMode switch
        {
            DeckRotationMode.Manual90 => 90,
            DeckRotationMode.Manual180 => 180,
            DeckRotationMode.Manual270 => 270,
            _ => 0
        };

        _deviceService.RotationMode = RotationMode;
        _deviceService.ManualRotationAngle = ManualRotationAngle;
        UpdateRotationDisplayState(_deviceService.EffectiveRotationAngle);
        ScheduleAutoSave();
    }

    private void OnBaseOrientationChanged(DeckOrientation orientation)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (RotationMode == DeckRotationMode.Auto)
            {
                UpdateRotationDisplayState(_deviceService.EffectiveRotationAngle);
            }
        });
    }

    private void OnEffectiveRotationChanged(DeckRotationAngle angle)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateRotationDisplayState(angle);
        });
    }

    private void UpdateRotationDisplayState(DeckRotationAngle angle)
    {
        IsPortraitLayout = (angle == DeckRotationAngle.Deg90 || angle == DeckRotationAngle.Deg270);
        KeypadColumns = IsPortraitLayout ? 3 : 5;
        KeypadRows = IsPortraitLayout ? 5 : 3;

        var loc = LocalizationService.Instance;
        string angleText = angle switch
        {
            DeckRotationAngle.Deg90 => loc.GetString("Orientation_90", "90° (Pionowa)"),
            DeckRotationAngle.Deg180 => loc.GetString("Orientation_180", "180° (Odwrócona)"),
            DeckRotationAngle.Deg270 => loc.GetString("Orientation_270", "270° (Pionowa)"),
            _ => loc.GetString("Orientation_0", "0° (Pozioma)")
        };

        OrientationStatusText = (RotationMode == DeckRotationMode.Auto)
            ? $"{angleText} {loc.GetString("Orientation_Auto", "[Auto]")}"
            : angleText;

        RefreshAllKeyVisuals();
    }

    private void OnStandbyStateChanged(bool isStandby)
    {
        if (!isStandby)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(350);
                var active = _profileService.GetActiveProfile();
                if (active != null)
                {
                    ApplyProfile(active, saveCurrent: false);
                }
                else
                {
                    PushAllHardwareState();
                }
            });
        }
    }

    public void PushAllHardwareState()
    {
        try
        {
            _deviceService.SetBrightness(KeypadBrightness);

            foreach (var key in Keys)
            {
                RenderKeyVisual(key, sendToHardware: true);
            }

            foreach (var slider in Sliders)
            {
                if (global::Avalonia.Media.Color.TryParse(slider.ColorHex, out var col))
                {
                    var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, slider.Brightness);
                    _deviceService.SetSliderLedColor(slider.Index, r, g, b);
                }
            }

            foreach (var roller in Rollers)
            {
                if (global::Avalonia.Media.Color.TryParse(roller.ColorHex, out var col))
                {
                    var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, roller.Brightness);
                    _deviceService.SetRollerLedColor(roller.Index, r, g, b);
                }
            }

            if (global::Avalonia.Media.Color.TryParse(BaseStation.ColorHex, out var baseCol))
            {
                var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(baseCol.R, baseCol.G, baseCol.B, BaseStation.Brightness);
                _deviceService.SetBaseLedColor(r, g, b);
            }

            foreach (var knob in Knobs)
            {
                if (global::Avalonia.Media.Color.TryParse(knob.ColorHex, out var col))
                {
                    var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, knob.Brightness);
                    _deviceService.SetKnobLedColor(knob.Index, r, g, b);
                }
                _deviceService.SetKnobLedMode(knob.Index, knob.LedMode);
                _deviceService.SetKnobLedBrightness(knob.Index, knob.Brightness);
            }

            if (Dial != null)
            {
                _deviceService.SetDialBrightness(Dial.Brightness);
                if (!string.IsNullOrEmpty(Dial.ImagePath) && System.IO.File.Exists(Dial.ImagePath))
                {
                    byte[] dialJpeg = ButtonImageRenderer.RenderDialImage(Dial.ImagePath, "Cover");
                    _ = _deviceService.SetDialImageAsync(dialJpeg);
                }
                else if (global::Avalonia.Media.Color.TryParse(Dial.ColorHex, out var dialCol))
                {
                    _deviceService.SetDialColor(dialCol.R, dialCol.G, dialCol.B);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Błąd wysyłania stanu do modułów: {ex.Message}");
        }
    }

    public void LoadCurrentViewKeys(ProfileModel profile)
    {
        List<KeyAssignmentModel>? sourceKeys = null;
        if (IsInsideFolder && CurrentFolderId != null)
        {
            var folder = profile.Folders?.FirstOrDefault(f => f.Id == CurrentFolderId);
            if (folder != null)
            {
                EnsureFolderKeysInitialized(folder);
                sourceKeys = folder.Keys;
            }
        }
        else
        {
            EnsurePagesInitialized(profile);
            if (CurrentPageIndex >= 0 && CurrentPageIndex < profile.Pages.Count)
            {
                sourceKeys = profile.Pages[CurrentPageIndex].Keys;
            }
        }

        if (sourceKeys == null)
        {
            if (IsInsideFolder)
            {
                sourceKeys = new List<KeyAssignmentModel>
                {
                    new()
                    {
                        Index = 0,
                        Action = DeckActionType.GoBack,
                        CustomTitle = "POWRÓT",
                        BackgroundColorHex = "#1E293B",
                        TextColorHex = "#FFFFFF"
                    }
                };
                for (int i = 1; i < 15; i++)
                {
                    sourceKeys.Add(new KeyAssignmentModel { Index = i, Action = DeckActionType.None });
                }
            }
            else
            {
                sourceKeys = profile.Keys;
            }
        }

        _autoSaveTimer.Stop();
        _isApplyingProfile = true;

        _animatedKeyRenderer.StopAllAnimations();
        Interlocked.Increment(ref _pageRenderVersion);

        foreach (var key in Keys)
        {
            if (key.AssignedAction == DeckActionType.StreamDeckPluginAction && !string.IsNullOrEmpty(key.StreamDeckPluginUuid))
            {
                _ = _streamDeckPluginHost?.SendWillDisappearAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid);
            }
        }

        try
        {
            for (int i = 0; i < Keys.Count; i++)
            {
                var targetKey = Keys[i];
                targetKey.IsPressed = false;
                targetKey.IsSelected = false;
                targetKey.SetRenderedJpeg(null);
                var k = sourceKeys.FirstOrDefault(x => x.Index == i);
                if (k != null)
                {
                    targetKey.AssignedAction = k.Action;
                    targetKey.ActionParameter = k.ActionParameter ?? "";
                    if (k.Action == DeckActionType.ConditionalAction && k.ConditionalAction != null && string.IsNullOrWhiteSpace(targetKey.ActionParameter))
                    {
                        targetKey.ActionParameter = k.ConditionalAction.ToParameterString();
                    }
                    targetKey.PressMode = k.PressMode;
                    targetKey.StateMode = k.StateMode;
                    targetKey.CustomTitle = k.CustomTitle ?? "";
                    targetKey.CustomSubtitle = k.CustomSubtitle ?? "";
                    targetKey.ActiveCustomTitle = k.ActiveCustomTitle ?? "";
                    targetKey.ActiveCustomSubtitle = k.ActiveCustomSubtitle ?? "";
                    targetKey.BackgroundColorHex = !string.IsNullOrEmpty(k.BackgroundColorHex) ? k.BackgroundColorHex : "#000000";
                    targetKey.TextColorHex = !string.IsNullOrEmpty(k.TextColorHex) ? k.TextColorHex : "#FFFFFF";
                    targetKey.SubtitleColorHex = !string.IsNullOrWhiteSpace(k.SubtitleColorHex) ? k.SubtitleColorHex : "#A1A1AA";
                    targetKey.ActiveBackgroundColorHex = k.ActiveBackgroundColorHex ?? "";
                    targetKey.ActiveTextColorHex = k.ActiveTextColorHex ?? "";
                    targetKey.ActiveSubtitleColorHex = k.ActiveSubtitleColorHex ?? "";
                    targetKey.ImagePath = k.ImagePath ?? "";
                    targetKey.ActiveImagePath = k.ActiveImagePath ?? "";
                    targetKey.ImageFitMode = !string.IsNullOrWhiteSpace(k.ImageFitMode) ? k.ImageFitMode : "Cover";
                    targetKey.ImageScale = k.ImageScale > 0 ? k.ImageScale : 100;
                    targetKey.ImageOffsetX = k.ImageOffsetX;
                    targetKey.ImageOffsetY = k.ImageOffsetY;
                    targetKey.ActiveImageFitMode = !string.IsNullOrWhiteSpace(k.ActiveImageFitMode) ? k.ActiveImageFitMode : "Cover";
                    targetKey.ActiveImageScale = k.ActiveImageScale > 0 ? k.ActiveImageScale : 100;
                    targetKey.ActiveImageOffsetX = k.ActiveImageOffsetX;
                    targetKey.ActiveImageOffsetY = k.ActiveImageOffsetY;
                    targetKey.FontFamily = string.IsNullOrWhiteSpace(k.FontFamily) ? "Segoe UI" : k.FontFamily;
                    targetKey.FontSize = k.FontSize;
                    targetKey.IsBold = k.IsBold;
                    targetKey.IsItalic = k.IsItalic;
                    targetKey.IsStrikeThrough = k.IsStrikeThrough;
                    targetKey.ObsSceneName = k.ObsSceneName ?? "";
                    targetKey.StreamDeckPluginUuid = k.StreamDeckPluginUuid ?? "";
                    targetKey.StreamDeckActionUuid = k.StreamDeckActionUuid ?? "";
                    targetKey.StreamDeckSettingsJson = k.StreamDeckSettingsJson ?? "";

                    if (targetKey.AssignedAction == DeckActionType.StreamDeckPluginAction && !string.IsNullOrEmpty(targetKey.StreamDeckPluginUuid))
                    {
                        _ = SendStreamDeckWillAppearAsync(targetKey);
                    }
                    if (targetKey.AssignedAction == DeckActionType.JetKvmPowerStatus ||
                        targetKey.AssignedAction == DeckActionType.JetKvmPowerShort)
                    {
                        targetKey.IsActiveToggleState = _jetKvmService.LastPowerState;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.SpotifyToggleShuffle)
                    {
                        targetKey.IsActiveToggleState = _spotifyService.CurrentState.IsShuffle;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.SpotifyToggleRepeat)
                    {
                        targetKey.IsActiveToggleState = _spotifyService.CurrentState.RepeatMode != SpotifyRepeatMode.Off;
                        targetKey.ActionParameter = _spotifyService.CurrentState.RepeatMode switch
                        {
                            SpotifyRepeatMode.Track => "REP 1",
                            SpotifyRepeatMode.Context => "REP ALL",
                            _ => "REP OFF"
                        };
                    }
                    else if (targetKey.AssignedAction == DeckActionType.SpotifyLikeTrack)
                    {
                        targetKey.IsActiveToggleState = _spotifyService.CurrentState.IsLiked;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VlcPlayPause)
                    {
                        targetKey.IsActiveToggleState = _vlcService.IsPlaying;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VlcFullscreenToggle)
                    {
                        targetKey.IsActiveToggleState = _vlcService.IsFullscreen;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodVoiceToggle)
                    {
                        targetKey.IsActiveToggleState = _voicemodService.IsVoiceChangerEnabled;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodMuteVoice)
                    {
                        targetKey.IsActiveToggleState = _voicemodService.IsMicMuted;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodHearMyVoiceToggle)
                    {
                        targetKey.IsActiveToggleState = _voicemodService.IsHearMyVoiceEnabled;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodBackgroundToggle)
                    {
                        targetKey.IsActiveToggleState = _voicemodService.IsBackgroundEffectsEnabled;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodVoiceSelect)
                    {
                        bool isCurrent = !string.IsNullOrWhiteSpace(targetKey.ActionParameter) &&
                                         (string.Equals(targetKey.ActionParameter, _voicemodService.CurrentVoiceId, StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(targetKey.ActionParameter, _voicemodService.CurrentVoiceName, StringComparison.OrdinalIgnoreCase));
                        targetKey.IsActiveToggleState = isCurrent;
                    }
                    else if (targetKey.AssignedAction is DeckActionType.HomeAssistantToggle or
                                                         DeckActionType.HomeAssistantTurnOn or
                                                         DeckActionType.HomeAssistantTurnOff)
                    {
                        var entity = _homeAssistantService?.GetEntity(targetKey.ActionParameter);
                        if (entity != null)
                        {
                            targetKey.IsActiveToggleState = entity.IsOn;
                        }
                    }
                }
                else
                {
                    targetKey.ClearAction();
                }
            }

            var page = (CurrentPageIndex >= 0 && profile.Pages != null && CurrentPageIndex < profile.Pages.Count)
                ? profile.Pages[CurrentPageIndex]
                : null;
            var effectiveWallpaperMode = !IsInsideFolder
                ? (page?.WallpaperMode ?? profile.WallpaperMode)
                : DeckWallpaperMode.PerKey;
            var effectiveWallpaperPath = !IsInsideFolder
                ? (page?.PanoramicWallpaperPath ?? profile.PanoramicWallpaperPath)
                : null;
            var effectiveFitMode = !IsInsideFolder
                ? (page?.PanoramicWallpaperFitMode ?? profile.PanoramicWallpaperFitMode ?? "Cover")
                : "Cover";

            WallpaperMode = effectiveWallpaperMode;
            PanoramicWallpaperPath = effectiveWallpaperPath;
            PanoramicWallpaperFitMode = effectiveFitMode;
            SyncPanoramicWallpaperStateToKeys();

            bool isPanoramic = effectiveWallpaperMode == DeckWallpaperMode.PanoramicDeck &&
                               !string.IsNullOrWhiteSpace(effectiveWallpaperPath) &&
                               System.IO.File.Exists(effectiveWallpaperPath);

            if (isPanoramic)
            {
                try
                {
                    byte[] panoJpeg = ButtonImageRenderer.RenderPanoramicImage(effectiveWallpaperPath!, effectiveFitMode);
                    _deviceService.SetPanoramicWallpaper(panoJpeg);
                    for (int i = 0; i < Keys.Count && i < 15; i++)
                    {
                        byte[] slice = ButtonImageRenderer.SlicePanoramicKeyJpeg(panoJpeg, i);
                        Keys[i].SetRenderedJpeg(slice);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Błąd ładowania tapety panoramicznej: {ex.Message}");
                    for (int i = 0; i < Keys.Count; i++)
                    {
                        RenderKeyVisual(Keys[i], sendToHardware: true);
                    }
                }
            }
            else
            {
                for (int i = 0; i < Keys.Count; i++)
                {
                    RenderKeyVisual(Keys[i], sendToHardware: true);
                }
            }

            OnPropertyChanged(nameof(SelectedKey));
        }
        finally
        {
            _isApplyingProfile = false;
        }
    }

    public void ApplyProfile(ProfileModel? profile, bool saveCurrent = true)
    {
        if (profile == null) return;

        if (saveCurrent && _hasLoadedProfile)
        {
            SaveCurrentStateToActiveProfile();
        }

        _navStack.Clear();
        _currentPageIndex = 0;
        _currentFolderId = null;
        _currentFolderName = "";

        EnsurePagesInitialized(profile);
        profile.Folders ??= new List<KeyFolderModel>();

        _autoSaveTimer.Stop();
        _isApplyingProfile = true;

        try
        {
            LoadCurrentViewKeys(profile);

            foreach (var s in profile.Sliders)
            {
                if (s.Index >= 0 && s.Index < Sliders.Count)
                {
                    var targetSlider = Sliders[s.Index];
                    targetSlider.SelectedTarget = s.DisplayTarget;
                    targetSlider.ColorHex = string.IsNullOrEmpty(s.ColorHex) ? "#38BDF8" : s.ColorHex;
                    targetSlider.Brightness = (byte)Math.Clamp(s.Brightness, 0, 100);
                }
            }

            foreach (var r in profile.Rollers)
            {
                if (r.Index >= 0 && r.Index < Rollers.Count)
                {
                    var targetRoller = Rollers[r.Index];
                    targetRoller.AssignedAction = r.Action;
                    if (!string.IsNullOrEmpty(r.ColorHex)) targetRoller.ColorHex = r.ColorHex;
                    targetRoller.Brightness = (byte)Math.Clamp(r.Brightness, 0, 100);
                }
            }

            if (profile.Knobs != null && profile.Knobs.Count > 0)
            {
                foreach (var k in profile.Knobs)
                {
                    if (k.Index >= 0 && k.Index < Knobs.Count)
                    {
                        var targetKnob = Knobs[k.Index];
                        targetKnob.RotateAction = k.RotateAction;
                        targetKnob.PressAction = k.PressAction;
                        targetKnob.PressActionParameter = k.PressActionParameter;
                        targetKnob.LedMode = k.LedMode;
                        if (!string.IsNullOrEmpty(k.ColorHex)) targetKnob.ColorHex = k.ColorHex;
                        targetKnob.Brightness = (byte)Math.Clamp(k.Brightness, 0, 100);
                        targetKnob.Speed = (byte)Math.Clamp(k.Speed, 0, 100);
                    }
                }
            }

            if (profile.Dial != null && Dial != null)
            {
                Dial.RotateAction = profile.Dial.RotateAction;
                Dial.PressAction = profile.Dial.PressAction;
                Dial.PressActionParameter = profile.Dial.PressActionParameter;
                if (!string.IsNullOrEmpty(profile.Dial.ColorHex)) Dial.ColorHex = profile.Dial.ColorHex;
                Dial.Brightness = (byte)Math.Clamp((int)profile.Dial.Brightness, 0, 100);
                Dial.ImagePath = profile.Dial.ImagePath;
                Dial.DisplayWidget = profile.Dial.DisplayWidget;
            }

            if (!string.IsNullOrEmpty(profile.LedColorHex))
            {
                BaseStation.ColorHex = profile.LedColorHex;
            }
            if (profile.BaseBrightness >= 0)
            {
                BaseStation.Brightness = (byte)Math.Clamp(profile.BaseBrightness, 0, 100);
            }
            if (profile.KeypadBrightness >= 0)
            {
                KeypadBrightness = (byte)Math.Clamp(profile.KeypadBrightness, 0, 100);
            }

            RotationMode = profile.RotationMode;
            ManualRotationAngle = profile.ManualRotationAngle;
            _deviceService.RotationMode = profile.RotationMode;
            _deviceService.ManualRotationAngle = profile.ManualRotationAngle;
            UpdateRotationDisplayState(_deviceService.EffectiveRotationAngle);

            _hasLoadedProfile = true;
            PushAllHardwareState();
            NotifyNavProperties();
        }
        finally
        {
            _isApplyingProfile = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _deviceService.KeyStateChanged -= OnKeyStateChanged;
        _deviceService.SliderValueChanged -= OnSliderValueChanged;
        _deviceService.RollerRotated -= OnRollerRotated;
        _deviceService.KnobRotated -= OnKnobRotated;
        _deviceService.KnobPressed -= OnKnobPressed;
        _deviceService.DialRotated -= OnDialRotated;
        _deviceService.DialPressed -= OnDialPressed;
        _deviceService.StandbyStateChanged -= OnStandbyStateChanged;
        _deviceService.BaseOrientationChanged -= OnBaseOrientationChanged;
        _deviceService.EffectiveRotationChanged -= OnEffectiveRotationChanged;
        _keyRenderer.KeyRendered -= OnKeyRenderedByService;
        _animatedKeyRenderer.FrameRenderedWithJpeg -= OnAnimatedFrameRendered;
        _deviceService.DevicesUpdated -= UpdateConnectionStates;
        _systemMonitorService.MetricsUpdated -= OnMetricsUpdated;

        _obsService.CurrentProgramSceneChanged -= OnObsSceneChanged;
        _obsService.StreamStateChanged -= OnObsStreamChanged;
        _obsService.RecordStateChanged -= OnObsRecordChanged;
        _discordService.VoiceStateChanged -= OnDiscordVoiceStateChanged;
        _youtubeService.StatusUpdated -= OnYouTubeStatusUpdated;
        _twitchService.EmoteOnlyStateChanged -= OnTwitchEmoteOnlyChanged;
        _twitchService.SubOnlyStateChanged -= OnTwitchSubOnlyChanged;
        _kickService.EmoteOnlyStateChanged -= OnKickEmoteOnlyChanged;
        _kickService.SubOnlyStateChanged -= OnKickSubOnlyChanged;
        _jetKvmService.PowerStateChanged -= OnJetKvmPowerStateChanged;
        _spotifyService.PlaybackStateChanged -= OnSpotifyPlaybackStateChanged;
        _vlcService.PlaybackStateChanged -= OnVlcPlaybackStateChanged;
        _voicemodService.StateChanged -= OnVoicemodStateChanged;
        _voicemodService.ConnectionStateChanged -= OnVoicemodConnectionChanged;
        _voicemodService.VoicesLoaded -= OnVoicemodVoicesLoaded;
        _voicemodService.SoundsLoaded -= OnVoicemodSoundsLoaded;
        _keyInteractionEngine.KeyTriggered -= OnKeyTriggered;
        _keyInteractionEngine.Dispose();

        if (_homeAssistantService != null)
        {
            _homeAssistantService.EntityStateChanged -= OnHomeAssistantEntityStateChanged;
        }

        _animatedKeyRenderer.StopAllAnimations();
    }

    public async Task SendStreamDeckWillAppearAsync(KeyItemViewModel key)
    {
        if (_streamDeckPluginHost == null || string.IsNullOrEmpty(key.StreamDeckPluginUuid)) return;
        await _streamDeckPluginHost.StartPluginAsync(key.StreamDeckPluginUuid);
        await _streamDeckPluginHost.SendWillAppearAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson);
    }

    public async Task SendStreamDeckWillDisappearAsync(int keyIndex, string pluginUuid, string actionUuid)
    {
        if (_streamDeckPluginHost == null || string.IsNullOrEmpty(pluginUuid)) return;
        await _streamDeckPluginHost.SendWillDisappearAsync(keyIndex, pluginUuid, actionUuid);
    }

    private void OnHomeAssistantEntityStateChanged(object? sender, HomeAssistantEntityModel entity)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if ((key.AssignedAction is DeckActionType.HomeAssistantToggle or
                                           DeckActionType.HomeAssistantTurnOn or
                                           DeckActionType.HomeAssistantTurnOff) &&
                    string.Equals(key.ActionParameter, entity.EntityId, StringComparison.OrdinalIgnoreCase))
                {
                    key.IsActiveToggleState = entity.IsOn;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }
}

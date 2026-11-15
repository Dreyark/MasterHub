using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.ViewModels.Inspector;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// Główny ViewModel aplikacji koordynujący nawigację, kanwę, profile i inspektora.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IMasterHubDeviceService _deviceService;
    private readonly IProfileService _profileService;

    [ObservableProperty]
    private string _appTitle = "Cooler Master MasterHub Studio";

    [ObservableProperty]
    private NavigationViewModel _navigation;

    [ObservableProperty]
    private CanvasViewModel _canvas;

    [ObservableProperty]
    private InspectorViewModel _inspector;

    [ObservableProperty]
    private MasterHub.Avalonia.ViewModels.Pages.ProfilesPageViewModel _profiles;

    [ObservableProperty]
    private MasterHub.Avalonia.ViewModels.Pages.ActionStorePageViewModel _actionStore;

    [ObservableProperty]
    private MasterHub.Avalonia.ViewModels.Pages.MacrosPageViewModel _macros;

    [ObservableProperty]
    private MasterHub.Avalonia.ViewModels.Pages.LogsPageViewModel _logs;

    [ObservableProperty]
    private MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel _settings;

    [ObservableProperty]
    private bool _isDeviceConnected;

    /// <summary>Nakładka potwierdzeń (usuń profil, przywróć kopię, wyłącz komputer…).</summary>
    [ObservableProperty]
    private ConfirmOverlayViewModel _confirmOverlay;

    /// <summary>Baner "nie udało się odczytać N plików" (kwarantanna uszkodzonych plików, T-02).</summary>
    public QuarantineBannerViewModel Quarantine { get; }

    /// <summary>Banners at the top of the window: failed actions, warnings (T-34 #8).</summary>
    public NoticeBannerViewModel Notices { get; } = new(MasterHub.Core.Notices.NoticeCenter.Shared);

    /// <summary>The draft on the deck (try-out, setup tour, a draft put back after a crash): banner with Keep / Throw away (T-08).</summary>
    public DraftSessionViewModel Draft { get; }

    /// <summary>The guided setup (T-08).</summary>
    public TourViewModel Tour { get; }

    /// <summary>The assistant (T-37): prepared profiles and suggestions. <c>null</c> when the app was built without its services.</summary>
    public AssistViewModel? Assist { get; }

    /// <summary>The update check (T-32): off until the user turns it on.</summary>
    public UpdatesViewModel? Updates { get; }

    public MainViewModel(
        NavigationViewModel navigation,
        CanvasViewModel canvas,
        InspectorViewModel inspector,
        MasterHub.Avalonia.ViewModels.Pages.ProfilesPageViewModel profiles,
        MasterHub.Avalonia.ViewModels.Pages.ActionStorePageViewModel actionStore,
        MasterHub.Avalonia.ViewModels.Pages.MacrosPageViewModel macros,
        MasterHub.Avalonia.ViewModels.Pages.LogsPageViewModel logs,
        MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel settings,
        IMasterHubDeviceService deviceService,
        IProfileService profileService,
        MasterHub.Core.Dialogs.IConfirmDialogService? confirmDialog = null,
        MasterHub.Core.Profiles.Templates.IProfileTemplateService? templates = null,
        MasterHub.Core.Apps.IInstalledAppsService? installedApps = null,
        MasterHub.Core.Profiles.Auto.IAssistSettingsStore? assistStore = null,
        MasterHub.Core.Profiles.Auto.IAppUsageService? appUsage = null,
        MasterHub.Core.Profiles.Auto.IPressCounter? pressCounter = null,
        MasterHub.Core.Profiles.Auto.IAutoProfileService? autoProfiles = null,
        MasterHub.Core.Profiles.Auto.INudgeService? nudges = null,
        MasterHub.Core.Updates.IUpdateService? updateService = null)
    {
        if (updateService != null)
        {
            Updates = new UpdatesViewModel(updateService, profileService);
            settings.Updates = Updates;
        }
        _navigation = navigation;
        _canvas = canvas;
        _inspector = inspector;
        _profiles = profiles;
        _actionStore = actionStore;
        _macros = macros;
        _logs = logs;
        _settings = settings;
        _deviceService = deviceService;
        _profileService = profileService;
        _confirmOverlay = new ConfirmOverlayViewModel(confirmDialog ?? new MasterHub.Core.Dialogs.ConfirmDialogService());

        _profiles.ProfileActivatedCallback = ApplyProfileToCanvas;
        Quarantine = new QuarantineBannerViewModel(profileService);

        // T-08: drafts, the setup tour, the template try-out
        Draft = new DraftSessionViewModel(profileService, canvas);
        Tour = new TourViewModel(Draft, profileService, templates, installedApps, deviceService);
        Draft.Ended += () => _profiles.RefreshProfiles();
        Tour.Kept += _ => _profiles.RefreshProfiles();
        // T-37: prepared profiles, suggestions, counting – all off until the user agrees
        if (assistStore != null && appUsage != null && pressCounter != null && autoProfiles != null && nudges != null)
        {
            Assist = new AssistViewModel(assistStore, appUsage, pressCounter, autoProfiles, nudges, profileService, Draft, Tour,
                installedApps, deviceService, templates);
            Assist.ProfilesChanged += () => _profiles.RefreshProfiles();
            Assist.OpenStudioRequested = () => _navigation.SelectPage(MasterHub.Avalonia.Services.NavigationPage.Studio);
            Assist.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AssistViewModel.UnseenCount)) _navigation.ProfilesBadge = Assist.UnseenCount;
            };
            _settings.Assist = Assist;
            _navigation.ProfilesBadge = Assist.UnseenCount;

            // the "new" marks are done with once the user has been on the Profiles page and left it
            var lastPage = _navigation.SelectedPage;
            _navigation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(NavigationViewModel.SelectedPage)) return;
                if (lastPage == MasterHub.Avalonia.Services.NavigationPage.Profiles && _navigation.SelectedPage != lastPage) Assist.MarkProfilesSeen();
                lastPage = _navigation.SelectedPage;
            };
        }

        _settings.RunTourRequested = () => { _navigation.SelectPage(MasterHub.Avalonia.Services.NavigationPage.Studio); Tour.Run(); };
        _profiles.Templates.PreviewRequested += profile =>
        {
            _navigation.SelectPage(MasterHub.Avalonia.Services.NavigationPage.Studio);
            Draft.Start(profile, MasterHub.Core.Profiles.Draft.DraftMode.Trial, 20);
        };
        _settings.Backups.Restored += () =>
        {
            _profiles.RefreshProfiles();
            ApplyProfileToCanvas(_profileService.GetActiveProfile());
            Quarantine.Refresh();
        };
        ApplyProfileToCanvas(_profileService.GetActiveProfile());

        UpdateConnectionState();
        _deviceService.DevicesUpdated += UpdateConnectionState;
        MasterHub.Core.Localization.LocalizationService.Instance.LanguageChanged += _ => OnPropertyChanged(nameof(ConnectionStatusText));
        Task.Run(() => _deviceService.Initialize());
    }

    /// <summary>Call once when the window is up: offers a draft that a crash left, otherwise the setup tour on a deck with nothing set up.</summary>
    public void OfferStartupHelp()
    {
        Draft.OfferPending();
        if (!Draft.IsVisible) Tour.OfferIfFirstRun();
        Assist?.Start();
        Updates?.Start();
    }

    private void ApplyProfileToCanvas(ProfileModel? profile)
    {
        Canvas.ApplyProfile(profile);
    }

    private readonly MasterHub.Core.Devices.DeckLinkStatus _link = new();
    private MasterHub.Core.Devices.DeckLinkPhase _linkPhase = MasterHub.Core.Devices.DeckLinkPhase.Connecting;

    /// <summary>"Connecting…" for the first seconds, then Connected / Disconnected (T-34 #13).</summary>
    public MasterHub.Core.Devices.DeckLinkPhase LinkPhase => _linkPhase;

    public string ConnectionStatusText
    {
        get
        {
            var loc = MasterHub.Core.Localization.LocalizationService.Instance;
            return _linkPhase switch
            {
                MasterHub.Core.Devices.DeckLinkPhase.Connected => loc.GetString("Common_Connected", "POŁĄCZONO"),
                MasterHub.Core.Devices.DeckLinkPhase.Connecting => loc.GetString("Common_Connecting", "ŁĄCZENIE…"),
                _ => loc.GetString("Common_Disconnected", "ODŁĄCZONO")
            };
        }
    }

    public string ConnectionStatusColor => _linkPhase switch
    {
        MasterHub.Core.Devices.DeckLinkPhase.Connected => "#00E5A3",
        MasterHub.Core.Devices.DeckLinkPhase.Connecting => "#FBBF24",
        _ => "#FF5252"
    };

    private void UpdateConnectionState()
    {
        IsDeviceConnected = _deviceService.IsKeypadConnected ||
                            _deviceService.IsSliderConnected ||
                            _deviceService.IsRollerConnected ||
                            _deviceService.IsBaseConnected;
        _linkPhase = _link.Evaluate(IsDeviceConnected);
        OnPropertyChanged(nameof(LinkPhase));
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(ConnectionStatusColor));

        // when nothing has answered yet, look again as soon as the grace time is over so "Connecting…" turns into the real answer
        if (_linkPhase == MasterHub.Core.Devices.DeckLinkPhase.Connecting && !_graceCheckScheduled)
        {
            _graceCheckScheduled = true;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                global::Avalonia.Threading.DispatcherTimer.RunOnce(UpdateConnectionState, _link.Grace + TimeSpan.FromMilliseconds(200)));
        }
    }

    private bool _graceCheckScheduled;
}

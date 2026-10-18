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
    private MasterHub.Avalonia.ViewModels.Pages.LogsPageViewModel _logs;

    [ObservableProperty]
    private MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel _settings;

    [ObservableProperty]
    private bool _isDeviceConnected;

    public MainViewModel(
        NavigationViewModel navigation,
        CanvasViewModel canvas,
        InspectorViewModel inspector,
        MasterHub.Avalonia.ViewModels.Pages.ProfilesPageViewModel profiles,
        MasterHub.Avalonia.ViewModels.Pages.ActionStorePageViewModel actionStore,
        MasterHub.Avalonia.ViewModels.Pages.LogsPageViewModel logs,
        MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel settings,
        IMasterHubDeviceService deviceService,
        IProfileService profileService)
    {
        _navigation = navigation;
        _canvas = canvas;
        _inspector = inspector;
        _profiles = profiles;
        _actionStore = actionStore;
        _logs = logs;
        _settings = settings;
        _deviceService = deviceService;
        _profileService = profileService;

        _profiles.ProfileActivatedCallback = ApplyProfileToCanvas;
        ApplyProfileToCanvas(_profileService.GetActiveProfile());

        UpdateConnectionState();
        _deviceService.DevicesUpdated += UpdateConnectionState;
        MasterHub.Core.Localization.LocalizationService.Instance.LanguageChanged += _ => OnPropertyChanged(nameof(ConnectionStatusText));
        Task.Run(() => _deviceService.Initialize());
    }

    private void ApplyProfileToCanvas(ProfileModel? profile)
    {
        Canvas.ApplyProfile(profile);
    }

    public string ConnectionStatusText => IsDeviceConnected
        ? MasterHub.Core.Localization.LocalizationService.Instance.GetString("Common_Connected", "POŁĄCZONO")
        : MasterHub.Core.Localization.LocalizationService.Instance.GetString("Common_Disconnected", "ODŁĄCZONO");
    public string ConnectionStatusColor => IsDeviceConnected ? "#00E5A3" : "#FF5252";

    private void UpdateConnectionState()
    {
        IsDeviceConnected = _deviceService.IsKeypadConnected ||
                            _deviceService.IsSliderConnected ||
                            _deviceService.IsRollerConnected ||
                            _deviceService.IsBaseConnected;
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(ConnectionStatusColor));
    }
}

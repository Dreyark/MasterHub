using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// ViewModel bocznego paska nawigacji: wybrana strona, tytuł bieżącej strony (okruszki w pasku tytułu),
/// zwijanie paska do samych ikon (zapamiętywane w ustawieniach aplikacji) i przełącznik uśpienia.
/// </summary>
public partial class NavigationViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IMasterHubDeviceService _deviceService;
    private readonly IProfileService? _profileService;

    [ObservableProperty]
    private NavigationPage _selectedPage = NavigationPage.Studio;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    /// <summary>Okno węższe niż próg (ustawia MainWindow): pasek wymuszony do samych ikon, żeby panele mieściły się na ekranie.</summary>
    [ObservableProperty]
    private bool _isAutoCollapsed;

    /// <summary>Pasek faktycznie wyświetlany jako zwinięty (ręcznie lub automatycznie).</summary>
    public bool IsSidebarNarrow => IsSidebarCollapsed || IsAutoCollapsed;

    partial void OnIsAutoCollapsedChanged(bool value) => OnPropertyChanged(nameof(IsSidebarNarrow));

    public bool IsStudioSelected => SelectedPage == NavigationPage.Studio;
    public bool IsProfilesSelected => SelectedPage == NavigationPage.Profiles;
    public bool IsActionStoreSelected => SelectedPage == NavigationPage.ActionStore;
    public bool IsLogsSelected => SelectedPage == NavigationPage.Logs;
    public bool IsMacrosSelected => SelectedPage == NavigationPage.Macros;
    public bool IsSettingsSelected => SelectedPage == NavigationPage.Settings;

    /// <summary>Nazwa bieżącej strony - ta sama co etykieta w pasku bocznym i tytuł nagłówka strony.</summary>
    public string CurrentPageTitle => LocalizationService.Instance.GetString(PageTitleKey(SelectedPage), SelectedPage.ToString());

    public static string PageTitleKey(NavigationPage page) => page switch
    {
        NavigationPage.Profiles => "NavLabel_Profiles",
        NavigationPage.ActionStore => "NavLabel_Store",
        NavigationPage.Macros => "NavLabel_Macros",
        NavigationPage.Logs => "NavLabel_Logs",
        NavigationPage.Settings => "NavLabel_Settings",
        _ => "NavLabel_Studio"
    };

    partial void OnSelectedPageChanged(NavigationPage value)
    {
        OnPropertyChanged(nameof(IsStudioSelected));
        OnPropertyChanged(nameof(IsProfilesSelected));
        OnPropertyChanged(nameof(IsActionStoreSelected));
        OnPropertyChanged(nameof(IsLogsSelected));
        OnPropertyChanged(nameof(IsMacrosSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(CurrentPageTitle));
    }

    [ObservableProperty]
    private bool _isStandby;

    /// <summary>How many prepared profiles are waiting on the Profiles page that the user has not looked at (T-37); 0 = no badge.</summary>
    [ObservableProperty]
    private int _profilesBadge;

    public bool HasProfilesBadge => ProfilesBadge > 0;

    partial void OnProfilesBadgeChanged(int value) => OnPropertyChanged(nameof(HasProfilesBadge));

    public NavigationViewModel(INavigationService navigationService, IMasterHubDeviceService deviceService,
        IProfileService? profileService = null)
    {
        _navigationService = navigationService;
        _deviceService = deviceService;
        _profileService = profileService;

        _selectedPage = _navigationService.CurrentPage;
        _isStandby = _deviceService.IsStandby;
        _isSidebarCollapsed = _profileService?.GetAppSettings().SidebarCollapsed ?? false;

        _navigationService.CurrentPageChanged += page => SelectedPage = page;
        _deviceService.StandbyStateChanged += standby => IsStandby = standby;
        LocalizationService.Instance.LanguageChanged += _ =>
        {
            OnPropertyChanged(nameof(CurrentPageTitle));
            OnPropertyChanged(nameof(SidebarToggleTip));
        };
    }

    [RelayCommand]
    public void SelectPage(NavigationPage page)
    {
        SelectedPage = page;
        _navigationService.NavigateTo(page);
    }

    [RelayCommand]
    public void ToggleStandby()
    {
        _deviceService.ToggleStandby();
        IsStandby = _deviceService.IsStandby;
    }

    [RelayCommand]
    public void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    public string SidebarToggleTip => LocalizationService.Instance.GetString(
        IsSidebarCollapsed ? "Nav_Expand" : "Nav_Collapse", IsSidebarCollapsed ? "Expand" : "Collapse");

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSidebarNarrow));
        OnPropertyChanged(nameof(SidebarToggleTip));
        if (_profileService == null) return;
        var settings = _profileService.GetAppSettings();
        if (settings.SidebarCollapsed == value) return;
        settings.SidebarCollapsed = value;
        _profileService.SaveAppSettings(settings);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// ViewModel dla lewego paska nawigacyjnego (Sidebar Rail).
/// </summary>
public partial class NavigationViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IMasterHubDeviceService _deviceService;

    [ObservableProperty]
    private NavigationPage _selectedPage = NavigationPage.Studio;

    public bool IsStudioSelected => SelectedPage == NavigationPage.Studio;
    public bool IsProfilesSelected => SelectedPage == NavigationPage.Profiles;
    public bool IsActionStoreSelected => SelectedPage == NavigationPage.ActionStore;
    public bool IsLogsSelected => SelectedPage == NavigationPage.Logs;
    public bool IsSettingsSelected => SelectedPage == NavigationPage.Settings;

    partial void OnSelectedPageChanged(NavigationPage value)
    {
        OnPropertyChanged(nameof(IsStudioSelected));
        OnPropertyChanged(nameof(IsProfilesSelected));
        OnPropertyChanged(nameof(IsActionStoreSelected));
        OnPropertyChanged(nameof(IsLogsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    [ObservableProperty]
    private bool _isStandby;

    public NavigationViewModel(INavigationService navigationService, IMasterHubDeviceService deviceService)
    {
        _navigationService = navigationService;
        _deviceService = deviceService;

        _selectedPage = _navigationService.CurrentPage;
        _isStandby = _deviceService.IsStandby;

        _navigationService.CurrentPageChanged += page => SelectedPage = page;
        _deviceService.StandbyStateChanged += standby => IsStandby = standby;
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
}

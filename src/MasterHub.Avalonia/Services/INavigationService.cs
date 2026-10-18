using System;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// Strony nawigacyjne dostępne w lewym pasku bocznym (Sidebar Rail).
/// </summary>
public enum NavigationPage
{
    Studio,
    Profiles,
    ActionStore,
    Settings,
    Logs
}

/// <summary>
/// Serwis zarządzania stanem aktywnej strony w aplikacji.
/// </summary>
public interface INavigationService
{
    NavigationPage CurrentPage { get; }
    event Action<NavigationPage>? CurrentPageChanged;
    void NavigateTo(NavigationPage page);
}

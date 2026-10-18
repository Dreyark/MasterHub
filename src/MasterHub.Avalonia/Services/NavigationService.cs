using System;
using Avalonia.Threading;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// Domyślna implementacja serwisu nawigacji dla lewego paska nawigacyjnego.
/// </summary>
public class NavigationService : INavigationService
{
    private NavigationPage _currentPage = NavigationPage.Studio;

    public NavigationPage CurrentPage => _currentPage;

    public event Action<NavigationPage>? CurrentPageChanged;

    public void NavigateTo(NavigationPage page)
    {
        if (_currentPage == page) return;
        _currentPage = page;
        Dispatcher.UIThread.Post(() => CurrentPageChanged?.Invoke(_currentPage));
    }
}

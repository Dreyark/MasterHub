using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Integrations.StreamDeck;

namespace MasterHub.Avalonia.ViewModels.Pages;

public partial class PluginCatalogViewModel : ObservableObject
{
    private readonly IPluginCatalogService _catalogService;
    private readonly IStreamDeckPluginHost? _streamDeckHost;
    private readonly Action? _onPluginInstalledCallback;
    private readonly List<PluginCatalogItemViewModel> _allEntries = new();
    private bool _hasLoadedOnce;

    public ObservableCollection<PluginCatalogItemViewModel> FilteredPlugins { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedCategory = "Wszystkie";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    public PluginCatalogViewModel(
        IPluginCatalogService catalogService,
        IStreamDeckPluginHost? streamDeckHost = null,
        Action? onPluginInstalledCallback = null)
    {
        _catalogService = catalogService;
        _streamDeckHost = streamDeckHost;
        _onPluginInstalledCallback = onPluginInstalledCallback;

        Categories.Add("Wszystkie");
    }

    public async Task EnsureCatalogLoadedAsync()
    {
        if (_hasLoadedOnce || IsLoading) return;
        await LoadCatalogAsync().ConfigureAwait(false);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task LoadCatalogAsync(bool forceRefresh = false)
    {
        if (IsLoading) return;
        _hasLoadedOnce = true;
        IsLoading = true;
        StatusMessage = "Ładowanie katalogu wtyczek online...";

        try
        {
            var entries = await _catalogService.GetCatalogAsync(forceRefresh).ConfigureAwait(false);
            var installedUuids = _streamDeckHost?.InstalledPlugins.Select(p => p.PluginUuid).ToHashSet(StringComparer.OrdinalIgnoreCase)
                                 ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var items = entries.Select(e => new PluginCatalogItemViewModel(e, installedUuids.Contains(e.Id))).ToList();

            var cats = entries
                .Select(e => string.IsNullOrWhiteSpace(e.Category) ? "Inne" : e.Category!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allEntries.Clear();
                _allEntries.AddRange(items);

                Categories.Clear();
                Categories.Add("Wszystkie");
                foreach (var c in cats) Categories.Add(c);

                ApplyFilter();
                StatusMessage = $"Załadowano {_allEntries.Count} wtyczek z katalogu online.";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = $"Błąd pobierania katalogu: {ex.Message}";
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    [RelayCommand]
    public async Task InstallPluginAsync(PluginCatalogItemViewModel? item)
    {
        if (item == null || item.IsInstalling || item.IsInstalled || _streamDeckHost == null) return;

        item.IsInstalling = true;
        StatusMessage = $"Pobieranie wtyczki {item.Name}...";

        try
        {
            string? pkgPath = await _catalogService.DownloadPluginPackageAsync(item.Entry).ConfigureAwait(false);
            if (string.IsNullOrEmpty(pkgPath) || !File.Exists(pkgPath))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = $"Błąd: Nie udało się pobrać pakietu dla {item.Name} z GitHub / repozytorium.";
                    item.IsInstalling = false;
                });
                return;
            }

            Dispatcher.UIThread.Post(() => StatusMessage = $"Instalowanie {item.Name}...");
            var installed = await _streamDeckHost.InstallPluginPackageAsync(pkgPath).ConfigureAwait(false);

            try { File.Delete(pkgPath); } catch { }

            if (installed != null)
            {
                await _streamDeckHost.StartPluginAsync(installed.PluginUuid).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() =>
                {
                    item.IsInstalled = true;
                    item.IsInstalling = false;
                    StatusMessage = $"Pomyślnie zainstalowano i uruchomiono: {item.Name}";
                    _onPluginInstalledCallback?.Invoke();
                });
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    item.IsInstalling = false;
                    StatusMessage = $"Błąd instalacji {item.Name}: nieprawidłowy format archiwum wtyczki.";
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                item.IsInstalling = false;
                StatusMessage = $"Błąd: {ex.Message}";
            });
        }
    }

    public void UpdateInstalledStates()
    {
        if (_streamDeckHost == null) return;
        var installedUuids = _streamDeckHost.InstalledPlugins.Select(p => p.PluginUuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _allEntries)
        {
            item.IsInstalled = installedUuids.Contains(item.Id);
        }
    }

    private void ApplyFilter()
    {
        FilteredPlugins.Clear();
        string query = SearchText?.Trim().ToLowerInvariant() ?? "";
        string cat = SelectedCategory ?? "Wszystkie";

        foreach (var item in _allEntries)
        {
            if (cat != "Wszystkie" && !item.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(query))
            {
                bool matches = item.Name.ToLowerInvariant().Contains(query) ||
                               item.Author.ToLowerInvariant().Contains(query) ||
                               item.Description.ToLowerInvariant().Contains(query);
                if (!matches) continue;
            }

            FilteredPlugins.Add(item);
        }
    }
}

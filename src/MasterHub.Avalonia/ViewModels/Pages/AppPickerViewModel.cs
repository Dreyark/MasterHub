using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Apps;
using MasterHub.Core.Localization;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>One row of the "Link app…" picker: a running app, or an installed app / game.</summary>
public sealed partial class AppPickerItemViewModel : ObservableObject
{
    public AppPickerItemViewModel(string title, string processName, string? exePath, string sourceLabel)
    {
        Title = title;
        ProcessName = processName;
        ExePath = exePath;
        SourceLabel = sourceLabel;
    }

    public string Title { get; }

    /// <summary>What the Smart Profile rule will match (process name without extension).</summary>
    public string ProcessName { get; }

    public string? ExePath { get; }
    public string SourceLabel { get; }
    public string Subtitle => $"{ProcessName} · {SourceLabel}";

    /// <summary>The app's real icon, filled in from a background task.</summary>
    [ObservableProperty]
    private Bitmap? _icon;
}

/// <summary>
/// Picker for linking an app to a profile: <b>running apps</b> and <b>installed apps / games</b> (T-36) with real icons,
/// so linking is a pick from a list and not typing an exe name. A name can still be typed for apps that are neither.
/// </summary>
public sealed partial class AppPickerViewModel : ObservableObject
{
    private readonly IInstalledAppsService? _installed;
    private readonly IAppIconExtractorService? _icons;
    private readonly List<AppPickerItemViewModel> _all = new();
    private readonly SemaphoreSlim _iconGate = new(2);
    private CancellationTokenSource _iconCts = new();

    public ObservableCollection<AppPickerItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _manualProcess = "";
    [ObservableProperty] private string _targetProfileName = "";
    [ObservableProperty] private bool _isScanning;

    /// <summary>Raised when the user picks an app.</summary>
    public event Action<AppPickerItemViewModel>? Picked;

    public AppPickerViewModel(IInstalledAppsService? installed = null, IAppIconExtractorService? icons = null)
    {
        _installed = installed;
        _icons = icons;
        if (_installed != null) _installed.Changed += () => Dispatcher.UIThread.Post(() => { if (IsOpen) Reload(); });
    }

    public void Open(string profileName)
    {
        TargetProfileName = profileName;
        Filter = "";
        ManualProcess = "";
        IsOpen = true;
        Reload();

        // The installed list is cached; scan again in the background the first time it is empty.
        if (_installed != null && _installed.Apps.Count == 0) _ = RefreshInstalledAsync();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        _iconCts.Cancel();
    }

    [RelayCommand]
    private void Pick(AppPickerItemViewModel? item)
    {
        if (item == null) return;
        Picked?.Invoke(item);
        Close();
    }

    [RelayCommand]
    private void AddManual()
    {
        string name = Path.GetFileNameWithoutExtension((ManualProcess ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name)) return;
        Pick(new AppPickerItemViewModel(name, name, null, LocalizationService.Instance.GetString("Picker_Manual", "typed")));
    }

    [RelayCommand]
    private async Task RefreshInstalledAsync()
    {
        if (_installed == null) return;
        IsScanning = true;
        try { await _installed.RefreshAsync(); }
        catch { /* a failed scan leaves the previous list in place */ }
        finally { IsScanning = false; }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void Reload()
    {
        _all.Clear();
        var loc = LocalizationService.Instance;
        string running = loc.GetString("Picker_Running", "running");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in RunningApps.List())
        {
            if (seen.Add(app.ProcessName))
                _all.Add(new AppPickerItemViewModel(app.ProcessName, app.ProcessName, app.ExePath, running));
        }

        if (_installed != null)
        {
            foreach (var app in _installed.Apps)
            {
                // Only apps whose executable is known can be matched against the foreground window.
                if (string.IsNullOrWhiteSpace(app.ProcessName) || !seen.Add(app.ProcessName)) continue;
                _all.Add(new AppPickerItemViewModel(app.Name, app.ProcessName, app.ExePath, app.Kind.ToString()));
            }
        }

        ApplyFilter();
        LoadIconsInBackground();
    }

    private void ApplyFilter()
    {
        Items.Clear();
        string f = (Filter ?? "").Trim();
        foreach (var item in _all.Where(i => f.Length == 0 ||
                                             i.Title.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                                             i.ProcessName.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            Items.Add(item);
        }
    }

    // Icons are extracted two at a time on background threads so opening the picker never blocks the UI.
    private void LoadIconsInBackground()
    {
        if (_icons == null) return;
        _iconCts.Cancel();
        _iconCts = new CancellationTokenSource();
        var token = _iconCts.Token;

        foreach (var item in _all.Where(i => !string.IsNullOrWhiteSpace(i.ExePath)).ToList())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _iconGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (token.IsCancellationRequested) return;
                        byte[]? png = _icons.ExtractIconPngBytes(item.ExePath!, 32);
                        if (png == null) return;
                        using var stream = new MemoryStream(png);
                        var bitmap = new Bitmap(stream);
                        Dispatcher.UIThread.Post(() => item.Icon = bitmap);
                    }
                    finally
                    {
                        _iconGate.Release();
                    }
                }
                catch { /* cancelled, or this exe has no usable icon */ }
            }, token);
        }
    }
}

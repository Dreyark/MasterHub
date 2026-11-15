using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.Updates;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// The update check card (T-32). Nothing is checked until the user turns it on and enters a repository; an update is only <b>announced</b> –
/// the button opens the release page in the browser, nothing is downloaded or installed by the app.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateService _service;
    private readonly IProfileService _profiles;
    private readonly Action<string> _openUrl;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(1) };
    private bool _loading;

    /// <param name="openUrl">Opens a page in the browser (tests give a recorder).</param>
    public UpdatesViewModel(IUpdateService service, IProfileService profiles, Action<string>? openUrl = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _openUrl = openUrl ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));

        var settings = _profiles.GetAppSettings().Updates;
        _loading = true;
        IsEnabled = settings.Enabled;
        Repository = settings.Repository;
        _loading = false;

        _service.Changed += OnResult;
        _timer.Tick += (_, _) => _ = CheckIfDueAsync();
        Refresh();
    }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _repository = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private bool _isChecking;

    private string _releaseUrl = "";

    public bool HasNotes => HasUpdate && Notes.Length > 0;

    partial void OnHasUpdateChanged(bool value) => OnPropertyChanged(nameof(HasNotes));
    partial void OnNotesChanged(string value) => OnPropertyChanged(nameof(HasNotes));

    partial void OnIsEnabledChanged(bool value)
    {
        if (_loading) return;
        Save();
        if (value) _ = CheckIfDueAsync();
        Refresh();
    }

    partial void OnRepositoryChanged(string value)
    {
        if (_loading) return;
        Save();
        Refresh();
    }

    /// <summary>Starts the periodic look (once an hour it asks whether a check is due; the service itself keeps the 6-hour rule). Does nothing while switched off.</summary>
    public void Start()
    {
        _timer.Start();
        _ = CheckIfDueAsync();
    }

    private void Save()
    {
        var app = _profiles.GetAppSettings();
        app.Updates.Enabled = IsEnabled;
        app.Updates.Repository = (Repository ?? "").Trim();
        _profiles.SaveAppSettings(app);
    }

    private async Task CheckIfDueAsync()
    {
        if (!_service.IsCheckDue()) return;
        await RunCheckAsync(userAsked: false);
    }

    [RelayCommand]
    private Task CheckNowAsync() => RunCheckAsync(userAsked: true);

    private async Task RunCheckAsync(bool userAsked)
    {
        if (IsChecking) return;

        IsChecking = true;
        StatusText = Text("Update_Checking", "Checking…");
        try { await _service.CheckAsync(userAsked); }
        catch (OperationCanceledException) { }
        finally { IsChecking = false; Refresh(); }
    }

    [RelayCommand]
    private void OpenRelease()
    {
        if (_releaseUrl.Length > 0) _openUrl(_releaseUrl);
    }

    private void OnResult(UpdateResult result) => Dispatcher.UIThread.Post(Refresh);

    /// <summary>Shows what the last check found.</summary>
    internal void Refresh()
    {
        var result = _service.Last;
        var settings = _profiles.GetAppSettings().Updates;

        HasUpdate = result?.IsUpdate == true;
        _releaseUrl = result?.IsUpdate == true ? result.Url ?? "" : "";
        Notes = result?.IsUpdate == true ? result.Notes ?? "" : "";

        if (IsChecking) return;

        StatusText = result?.Outcome switch
        {
            UpdateOutcome.Available => string.Format(Text("Update_Available", "Version {0} is available."), result.LatestVersion),
            UpdateOutcome.UpToDate => Text("Update_UpToDate", "You have the latest version."),
            UpdateOutcome.Failed => string.Format(Text("Update_Failed", "The check did not work: {0}"), result.Problem),
            _ when !UpdateService.IsValidRepository(Repository) => Text("Update_NotConfigured", "Enter the GitHub repository (owner/name) to check."),
            _ when settings.LastCheckUtc is null => Text("Update_NeverChecked", "Not checked yet."),
            _ => ""
        };
    }

    public bool CanCheck => !IsChecking && UpdateService.IsValidRepository(Repository);
    partial void OnIsCheckingChanged(bool value) => OnPropertyChanged(nameof(CanCheck));

    private static string Text(string key, string fallback) => LocalizationService.Instance.GetString(key, fallback);

    public void Dispose()
    {
        _timer.Stop();
        _service.Changed -= OnResult;
    }
}

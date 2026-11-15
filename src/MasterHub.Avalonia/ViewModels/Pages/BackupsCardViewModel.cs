using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Dialogs;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Backup;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>One row of the backups list.</summary>
public sealed class BackupItemViewModel
{
    public BackupItemViewModel(BackupInfo info)
    {
        Info = info;
        var loc = LocalizationService.Instance;
        When = info.CreatedLocal.ToString("g", CultureInfo.CurrentCulture);
        Reason = loc.GetString($"Backups_Reason_{info.Reason}", info.Reason);
        ProfilesText = string.Format(loc.GetString("Backups_ProfileCount", "{0} profiles"), info.ProfileCount);
    }

    public BackupInfo Info { get; }
    public string Name => Info.Name;
    public string When { get; }
    public string Reason { get; }
    public string ProfilesText { get; }
}

/// <summary>Settings → Backups: list snapshots, back up now, restore (with confirmation), open the folder, optional mirror folder.</summary>
public sealed partial class BackupsCardViewModel : ObservableObject
{
    private readonly IProfileService _profiles;
    private readonly IConfirmDialogService _confirm;

    public ObservableCollection<BackupItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private string _mirrorPath;

    /// <summary>Raised after a successful restore so the app can reload the active profile.</summary>
    public event Action? Restored;

    public BackupsCardViewModel(IProfileService profiles, IConfirmDialogService? confirm = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _confirm = confirm ?? new AutoConfirmDialogService();
        _mirrorPath = profiles.GetAppSettings().BackupMirrorPath ?? "";
        Refresh();
    }

    partial void OnMirrorPathChanged(string value)
    {
        var settings = _profiles.GetAppSettings();
        if ((settings.BackupMirrorPath ?? "") == (value ?? "")) return;
        settings.BackupMirrorPath = value?.Trim() ?? "";
        _profiles.SaveAppSettings(settings);
    }

    [RelayCommand]
    public void Refresh()
    {
        Items.Clear();
        foreach (var info in _profiles.Backups.List()) Items.Add(new BackupItemViewModel(info));
        HasItems = Items.Count > 0;
    }

    [RelayCommand]
    public void BackUpNow()
    {
        var info = _profiles.Backups.CreateSnapshot("manual");
        var loc = LocalizationService.Instance;
        StatusMessage = info != null
            ? loc.GetString("Backups_Created", "Backup created.")
            : loc.GetString("Backups_CreateFailed", "Nothing to back up, or the copy failed.");
        Refresh();
    }

    [RelayCommand]
    public async Task RestoreAsync(BackupItemViewModel? item)
    {
        if (item == null) return;
        var loc = LocalizationService.Instance;

        if (!await _confirm.ConfirmAsync(ConfirmRequests.RestoreBackup(item.When + " · " + item.Reason))) return;

        bool ok = _profiles.RestoreBackup(item.Name);
        StatusMessage = ok
            ? loc.GetString("Backups_Restored", "Backup restored. Restart the app so every integration reloads its settings.")
            : loc.GetString("Backups_RestoreFailed", "This backup could not be restored.");
        Refresh();
        if (ok) Restored?.Invoke();
    }

    [RelayCommand]
    public void OpenFolder()
    {
        try
        {
            string dir = _profiles.Backups.BackupsDirectory;
            System.IO.Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}

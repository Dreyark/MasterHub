using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// Banner "N files could not be read": unreadable profile/settings files are renamed to
/// <c>*.corrupt-&lt;time&gt;</c> (never deleted) – the banner says so and offers to open the folder.
/// </summary>
public sealed partial class QuarantineBannerViewModel : ObservableObject
{
    private readonly IProfileService _profiles;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _text = "";

    public QuarantineBannerViewModel(IProfileService profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        Refresh();
    }

    public void Refresh()
    {
        int count = _profiles.QuarantinedFiles.Count;
        IsVisible = count > 0;
        Text = count == 0
            ? ""
            : string.Format(LocalizationService.Instance.GetString("Quarantine_Banner",
                "{0} file(s) could not be read and were set aside as *.corrupt-… (nothing was deleted)."), count);
    }

    [RelayCommand]
    private void Dismiss() => IsVisible = false;

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            string? file = _profiles.QuarantinedFiles.FirstOrDefault();
            string? dir = file == null ? null : Path.GetDirectoryName(file);
            if (dir != null) Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch { /* explorer not available */ }
    }
}

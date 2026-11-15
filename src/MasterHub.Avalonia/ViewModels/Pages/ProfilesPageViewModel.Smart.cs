using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>
/// Smart Profiles on the Profiles page (T-01): feeds the rules of every profile to <see cref="ISmartProfileService"/>,
/// activates the profile it asks for and returns to the <i>last profile the user picked</i> when no rule matches.
/// "Active" is not "Home": a manual pick never changes Home, and an automatic switch is never remembered.
/// </summary>
public partial class ProfilesPageViewModel
{
    /// <summary>Global Auto-switch toggle (also in the tray menu). Persisted in the app settings.</summary>
    [ObservableProperty]
    private bool _autoSwitchEnabled = true;

    private void InitializeSmartProfiles()
    {
        AutoSwitchEnabled = _profileService.GetAppSettings().AutoSwitchProfiles;
        if (_smart == null) return;

        _smart.IsEnabled = AutoSwitchEnabled;
        _smart.FallbackProfileId = _profileService.GetActiveProfile().Id; // the profile loaded at start is the last manual pick
        _smart.AutoSwitchProfileRequested += OnAutoSwitchRequested;
        _smart.IsEnabledChanged += enabled => Dispatcher.UIThread.Post(() =>
        {
            AutoSwitchEnabled = enabled; // e.g. toggled from the tray
        });
    }

    partial void OnAutoSwitchEnabledChanged(bool value)
    {
        var settings = _profileService.GetAppSettings();
        if (settings.AutoSwitchProfiles != value)
        {
            settings.AutoSwitchProfiles = value;
            _profileService.SaveAppSettings(settings);
        }

        if (_smart != null && _smart.IsEnabled != value) _smart.IsEnabled = value;
    }

    /// <summary>Gives every enabled rule of every profile to the watcher (no hook runs when there are none).</summary>
    private void RebuildSmartRules()
    {
        if (_smart == null) return;

        var rules = new List<SmartProfileRule>();
        foreach (var profile in _profileService.GetAllProfiles())
        {
            foreach (var rule in profile.SmartRules ?? new())
            {
                rule.ProfileId = profile.Id; // a rule always belongs to the profile it is stored in
                rules.Add(rule);
            }
        }

        _smart.UpdateRules(rules);
    }

    private void RememberManualPick(string profileId)
    {
        if (_smart != null) _smart.FallbackProfileId = profileId;
    }

    private void OnAutoSwitchRequested(string profileId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_profileService.ActiveDraft != null) return;   // a draft is on the deck (T-08): auto-switching would throw it away

            var item = Profiles.FirstOrDefault(p => p.Id == profileId);
            if (item == null || item.IsActive) return;

            // Automatic: kept in memory only, so after a restart the app comes back to the last manually picked profile.
            _profileService.SetActiveProfile(profileId, persist: false);
            MarkActive(item);
            ProfileActivatedCallback?.Invoke(item.Profile);
        });
    }

    // ── linking apps ────────────────────────────────────────────────────────────────────────

    private ProfileItemViewModel? _linkTarget;

    [RelayCommand]
    private void OpenPicker(ProfileItemViewModel? item)
    {
        if (item == null) return;
        _linkTarget = item;
        Picker.Open(item.Name);
    }

    private void LinkApp(AppPickerItemViewModel app)
    {
        var target = _linkTarget;
        if (target == null || string.IsNullOrWhiteSpace(app.ProcessName)) return;

        // Re-read from disk: the Studio may have saved newer key assignments since this page was loaded.
        var fresh = _profileService.GetAllProfiles().FirstOrDefault(p => p.Id == target.Id);
        if (fresh == null) return;

        var rules = fresh.SmartRules ??= new List<SmartProfileRule>();
        bool exists = rules.Any(r => string.Equals(r.ProcessName, app.ProcessName, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(r.WindowTitle));
        if (!exists)
        {
            rules.Add(new SmartProfileRule { ProfileId = target.Id, ProcessName = app.ProcessName });
            _profileService.SaveProfile(fresh);
        }

        StatusIsError = false;
        StatusMessage = string.Format(LocalizationService.Instance.GetString("Profiles_AppLinked", "{0} is now linked to profile \"{1}\"."), app.ProcessName, target.Name);
        RefreshProfiles();
    }

    [RelayCommand]
    private void UnlinkApp(LinkedAppItemViewModel? app)
    {
        if (app == null) return;
        var owner = Profiles.FirstOrDefault(p => p.LinkedApps.Contains(app));
        if (owner == null) return;

        var fresh = _profileService.GetAllProfiles().FirstOrDefault(p => p.Id == owner.Id);
        if (fresh == null) return;

        fresh.SmartRules?.RemoveAll(r => r.Id == app.Rule.Id);
        _profileService.SaveProfile(fresh);
        RefreshProfiles();
    }
}

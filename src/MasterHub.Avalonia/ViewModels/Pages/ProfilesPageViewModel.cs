using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Apps;
using MasterHub.Core.Dialogs;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>An app linked to a profile (one Smart Profile rule), shown as a chip on the profile row.</summary>
public sealed class LinkedAppItemViewModel
{
    public LinkedAppItemViewModel(SmartProfileRule rule)
    {
        Rule = rule;
        Name = string.IsNullOrWhiteSpace(rule.WindowTitle) ? rule.ProcessName : $"{rule.ProcessName} · {rule.WindowTitle}";
    }

    public SmartProfileRule Rule { get; }
    public string Name { get; }
}

public partial class ProfileItemViewModel : ObservableObject
{
    public ProfileModel Profile { get; }
    public string Id => Profile.Id;
    public string Name => Profile.Name;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Group heading: Home / Your profiles / For apps and games.</summary>
    [ObservableProperty]
    private string _groupLabel = "";

    /// <summary>True for the first profile of a group (the list shows the heading above it).</summary>
    [ObservableProperty]
    private bool _showGroupHeader;

    public ObservableCollection<LinkedAppItemViewModel> LinkedApps { get; } = new();

    /// <summary>Prepared by the assistant and still switched off (T-37): shown under "Ready for your apps" with a Switch on button.</summary>
    [ObservableProperty]
    private bool _isReady;

    /// <summary>Prepared by the assistant and not looked at yet: carries a "New" mark.</summary>
    [ObservableProperty]
    private bool _isNew;

    /// <summary>"Not linked yet: it switches in with OBS once you switch it on."</summary>
    public string ReadyHint { get; set; } = "";

    public bool HasLinkedApps => LinkedApps.Count > 0;

    public ProfileItemViewModel(ProfileModel profile, bool isActive)
    {
        Profile = profile;
        _isActive = isActive;
        foreach (var rule in profile.SmartRules ?? new()) LinkedApps.Add(new LinkedAppItemViewModel(rule));
    }
}

public partial class ProfilesPageViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly IConfirmDialogService _confirmDialog;
    private readonly ISmartProfileService? _smart;
    private readonly MasterHub.Core.Profiles.Auto.IAutoProfileService? _auto;
    private readonly MasterHub.Core.Profiles.Packages.ProfilePackageService? _packages;

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

    /// <summary>The "Link app…" picker (running apps + installed apps/games).</summary>
    public AppPickerViewModel Picker { get; }

    /// <summary>The "From a ready-made profile" picker (T-10).</summary>
    public TemplatePickerViewModel Templates { get; }

    [ObservableProperty]
    private ProfileItemViewModel? _selectedProfile;

    [ObservableProperty]
    private string _newProfileName = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _statusIsError;

    public Action<ProfileModel>? ProfileActivatedCallback { get; set; }

    public ProfilesPageViewModel(
        IProfileService profileService,
        IConfirmDialogService? confirmDialog = null,
        ISmartProfileService? smartProfiles = null,
        IInstalledAppsService? installedApps = null,
        IAppIconExtractorService? iconExtractor = null,
        MasterHub.Core.Profiles.Packages.ProfilePackageService? packages = null,
        MasterHub.Core.Profiles.Templates.IProfileTemplateService? templates = null,
        MasterHub.Core.Profiles.Auto.IAutoProfileService? autoProfiles = null)
    {
        _auto = autoProfiles;
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _confirmDialog = confirmDialog ?? new AutoConfirmDialogService();
        _smart = smartProfiles;
        _packages = packages;
        Picker = new AppPickerViewModel(installedApps, iconExtractor);
        Picker.Picked += LinkApp;

        // "New profile -> From a ready-made profile" (T-10)
        Templates = new TemplatePickerViewModel(templates);
        Templates.Added += profile =>
        {
            RefreshProfiles();
            var item = Profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (item != null) ActivateProfile(item);
            StatusIsError = false;
            StatusMessage = string.Format(LocalizationService.Instance.GetString("Templates_Added", "Profile \"{0}\" was added from a ready-made profile."), profile.Name);
        };

        InitializeSmartProfiles();
        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        Profiles.Clear();
        var all = _profileService.GetAllProfiles();
        var active = _profileService.GetActiveProfile();

        // Home (the default profile), then the user's own profiles, then profiles linked to apps/games.
        // A profile the assistant prepared and that is still switched off (T-37) has its own group: it does nothing until switched on.
        static bool IsReady(ProfileModel p) => MasterHub.Core.Profiles.Auto.AutoProfileService.IsReady(p);
        static bool IsForApps(ProfileModel p) => !p.IsDefault && !IsReady(p) && p.SmartRules?.Any(r => !string.IsNullOrWhiteSpace(r.ProcessName)) == true;
        var loc = LocalizationService.Instance;
        var unseen = (_auto?.UnseenProfileIds() ?? Array.Empty<string>()).ToHashSet();
        var groups = new (string Label, Func<ProfileModel, bool> Match)[]
        {
            (loc.GetString("Profiles_GroupHome", "Home"), p => p.IsDefault),
            (loc.GetString("Profiles_GroupReady", "Ready for your apps"), IsReady),
            (loc.GetString("Profiles_GroupYours", "Your profiles"), p => !p.IsDefault && !IsReady(p) && !IsForApps(p)),
            (loc.GetString("Profiles_GroupApps", "For apps and games"), IsForApps)
        };

        foreach (var (label, match) in groups)
        {
            bool first = true;
            foreach (var p in all.Where(match))
            {
                bool isActive = (active != null && p.Id == active.Id);
                var item = new ProfileItemViewModel(p, isActive) { GroupLabel = label, ShowGroupHeader = first, IsReady = IsReady(p), IsNew = unseen.Contains(p.Id) };
                if (item.IsReady)
                    item.ReadyHint = string.Format(loc.GetString("Profiles_ReadyHint", "Not linked yet: it switches in with {0} once you switch it on."),
                        string.Join(", ", p.SmartRules.Select(r => r.ProcessName)));
                first = false;
                Profiles.Add(item);
                if (isActive) SelectedProfile = item;
            }
        }

        if (SelectedProfile == null && Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
        }

        RebuildSmartRules();
    }

    /// <summary>"Switch on" on a prepared profile (T-37): its rules turn on, so it switches in with its programs from now on.</summary>
    [RelayCommand]
    private void SwitchOn(ProfileItemViewModel? item)
    {
        if (item == null || _auto == null) return;
        if (!_auto.SwitchOn(item.Id)) return;

        StatusIsError = false;
        StatusMessage = string.Format(
            LocalizationService.Instance.GetString("Profiles_SwitchedOn", "Profile \"{0}\" is switched on: it now switches in with {1}."),
            item.Name, string.Join(", ", item.Profile.SmartRules.Select(r => r.ProcessName)));
        RefreshProfiles();
    }

    [RelayCommand]
    public void CreateProfile()
    {
        string name = string.IsNullOrWhiteSpace(NewProfileName) ? $"Profil {Profiles.Count + 1}" : NewProfileName.Trim();
        var created = _profileService.CreateProfile(name, setAsActive: true);
        NewProfileName = "";
        RememberManualPick(created.Id);
        RefreshProfiles();
        ProfileActivatedCallback?.Invoke(created);
    }

    /// <summary>The user picked a profile (a click). Selecting a profile only switches to it – it never changes Home.</summary>
    [RelayCommand]
    public void ActivateProfile(ProfileItemViewModel item)
    {
        if (item == null) return;
        _profileService.SetActiveProfile(item.Id);
        RememberManualPick(item.Id);
        MarkActive(item);
        ProfileActivatedCallback?.Invoke(item.Profile);
    }

    private void MarkActive(ProfileItemViewModel item)
    {
        foreach (var p in Profiles)
        {
            p.IsActive = (p.Id == item.Id);
        }
        SelectedProfile = item;
    }

    [RelayCommand]
    public async System.Threading.Tasks.Task DeleteProfileAsync(ProfileItemViewModel item)
    {
        if (item == null) return;
        if (!await _confirmDialog.ConfirmAsync(MasterHub.Avalonia.Services.ConfirmRequests.DeleteProfile(item.Name))) return;
        _profileService.DeleteProfile(item.Id);
        var active = _profileService.GetActiveProfile();
        if (_smart != null && _smart.FallbackProfileId == item.Id) _smart.FallbackProfileId = active.Id;
        RefreshProfiles();
        ProfileActivatedCallback?.Invoke(active);
    }

    public void ExportProfile(ProfileItemViewModel item, string targetFilePath)
    {
        if (item == null || string.IsNullOrWhiteSpace(targetFilePath)) return;
        try
        {
            if (_packages != null && targetFilePath.EndsWith(MasterHub.Core.Profiles.Packages.ProfilePackageService.Extension, StringComparison.OrdinalIgnoreCase))
                _packages.Export(item.Id, targetFilePath);   // .mhdeck: profile + pictures, no credentials
            else
                _profileService.ExportProfileToFile(item.Id, targetFilePath);   // settings only
            StatusIsError = false;
            StatusMessage = LocalizationService.Instance.GetString("Profiles_ExportSuccess", "Pomyślnie wyeksportowano profil do pliku JSON (bez wrażliwych haseł).");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Błąd eksportu: {ex.Message}";
        }
    }

    public void ImportProfile(string sourceFilePath) => _ = ImportProfileAsync(sourceFilePath);

    public async System.Threading.Tasks.Task ImportProfileAsync(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath)) return;
        if (_packages != null && sourceFilePath.EndsWith(MasterHub.Core.Profiles.Packages.ProfilePackageService.Extension, StringComparison.OrdinalIgnoreCase))
        {
            await ImportPackageAsync(sourceFilePath);
            return;
        }

        try
        {
            var imported = _profileService.ImportProfileFromFile(sourceFilePath);
            RefreshProfiles();
            var importedItem = Profiles.FirstOrDefault(p => p.Id == imported.Id);
            if (importedItem != null)
            {
                ActivateProfile(importedItem);
            }
            StatusIsError = false;
            StatusMessage = LocalizationService.Instance.GetString("Profiles_ImportSuccess", "Pomyślnie zaimportowano profil.");
            await RunCheckAsync(imported);   // T-34 #9: what this PC lacks for the imported profile
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = LocalizationService.Instance.GetString("Profiles_ImportError", "Błąd podczas importu profilu: ") + ex.Message;
        }
    }
}

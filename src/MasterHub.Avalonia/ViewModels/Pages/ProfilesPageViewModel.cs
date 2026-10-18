using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.ViewModels.Pages;

public partial class ProfileItemViewModel : ObservableObject
{
    public ProfileModel Profile { get; }
    public string Id => Profile.Id;
    public string Name => Profile.Name;

    [ObservableProperty]
    private bool _isActive;

    public ProfileItemViewModel(ProfileModel profile, bool isActive)
    {
        Profile = profile;
        _isActive = isActive;
    }
}

public partial class ProfilesPageViewModel : ObservableObject
{
    private readonly IProfileService _profileService;

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

    [ObservableProperty]
    private ProfileItemViewModel? _selectedProfile;

    [ObservableProperty]
    private string _newProfileName = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _statusIsError;

    public Action<ProfileModel>? ProfileActivatedCallback { get; set; }

    public ProfilesPageViewModel(IProfileService profileService)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        Profiles.Clear();
        var all = _profileService.GetAllProfiles();
        var active = _profileService.GetActiveProfile();

        foreach (var p in all)
        {
            bool isActive = (active != null && p.Id == active.Id);
            var item = new ProfileItemViewModel(p, isActive);
            Profiles.Add(item);
            if (isActive)
            {
                SelectedProfile = item;
            }
        }

        if (SelectedProfile == null && Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
        }
    }

    [RelayCommand]
    public void CreateProfile()
    {
        string name = string.IsNullOrWhiteSpace(NewProfileName) ? $"Profil {Profiles.Count + 1}" : NewProfileName.Trim();
        var created = _profileService.CreateProfile(name, setAsActive: true);
        NewProfileName = "";
        RefreshProfiles();
        ProfileActivatedCallback?.Invoke(created);
    }

    [RelayCommand]
    public void ActivateProfile(ProfileItemViewModel item)
    {
        if (item == null) return;
        _profileService.SetActiveProfile(item.Id);
        foreach (var p in Profiles)
        {
            p.IsActive = (p.Id == item.Id);
        }
        SelectedProfile = item;
        ProfileActivatedCallback?.Invoke(item.Profile);
    }

    [RelayCommand]
    public void DeleteProfile(ProfileItemViewModel item)
    {
        if (item == null) return;
        _profileService.DeleteProfile(item.Id);
        RefreshProfiles();
        var active = _profileService.GetActiveProfile();
        ProfileActivatedCallback?.Invoke(active);
    }

    public void ExportProfile(ProfileItemViewModel item, string targetFilePath)
    {
        if (item == null || string.IsNullOrWhiteSpace(targetFilePath)) return;
        try
        {
            _profileService.ExportProfileToFile(item.Id, targetFilePath);
            StatusIsError = false;
            StatusMessage = LocalizationService.Instance.GetString("Profiles_ExportSuccess", "Pomyślnie wyeksportowano profil do pliku JSON (bez wrażliwych haseł).");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Błąd eksportu: {ex.Message}";
        }
    }

    public void ImportProfile(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath)) return;
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
            StatusMessage = LocalizationService.Instance.GetString("Profiles_ImportSuccess", "Pomyślnie zaimportowano profil z pliku JSON.");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = LocalizationService.Instance.GetString("Profiles_ImportError", "Błąd podczas importu profilu: ") + ex.Message;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Localization;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels;

public partial class KeyItemViewModel
{

    public Action<KeyItemViewModel, DeckActionType, string?>? NavigationRequestedCallback { get; set; }
    public Action<string>? FolderDeletedCallback { get; set; }

    public IReadOnlyList<string> AvailableProfiles
    {
        get
        {
            var profileService = GetService<IProfileService>();
            var profiles = profileService?.GetAllProfiles();
            if (profiles != null && profiles.Count > 0)
            {
                return profiles.Select(p => p.Name).ToList();
            }
            return Array.Empty<string>();
        }
    }

    public string? SelectedProfile
    {
        get => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : null;
        set
        {
            ActionParameter = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            ActionChangedCallback?.Invoke(this);
        }
    }

    public IReadOnlyList<string> AvailablePages
    {
        get
        {
            var profileService = GetService<IProfileService>();
            var activeProf = profileService?.GetActiveProfile();
            if (activeProf?.Pages != null && activeProf.Pages.Count > 0)
            {
                return activeProf.Pages.Select((p, idx) => $"Strona {idx + 1}").ToList();
            }
            return new[] { "Strona 1" };
        }
    }

    public string? SelectedPage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ActionParameter))
            {
                var match = AvailablePages.FirstOrDefault(p => p.Equals(ActionParameter, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;

                if (int.TryParse(ActionParameter, out int idx) && idx >= 1 && idx <= AvailablePages.Count)
                {
                    return $"Strona {idx}";
                }
            }
            return null;
        }
        set
        {
            ActionParameter = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            ActionChangedCallback?.Invoke(this);
        }
    }

    public IReadOnlyList<string> AvailableFolders
    {
        get
        {
            var profileService = GetService<IProfileService>();
            var activeProf = profileService?.GetActiveProfile();
            var list = new List<string>();
            if (activeProf?.Folders != null)
            {
                foreach (var f in activeProf.Folders)
                {
                    if (!string.IsNullOrWhiteSpace(f.Name) && !list.Contains(f.Name))
                    {
                        list.Add(f.Name);
                    }
                }
            }
            return list;
        }
    }

    public string? SelectedFolder
    {
        get => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ActionParameter = value;
                OnPropertyChanged(nameof(FolderNameInput));
                EnsureFolderExistsInProfile(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string FolderNameInput
    {
        get => ActionParameter;
        set
        {
            string clean = value ?? "";
            if (ActionParameter != clean)
            {
                string oldName = ActionParameter;
                ActionParameter = clean;
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    RenameOrEnsureFolderInProfile(oldName, clean);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedFolder));
                OnPropertyChanged(nameof(AvailableFolders));
                OnPropertyChanged(nameof(DisplayTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    [RelayCommand]
    public void EnterFolder()
    {
        if (AssignedAction == DeckActionType.OpenFolder)
        {
            string target = !string.IsNullOrWhiteSpace(FolderNameInput) ? FolderNameInput.Trim() : ActionParameter.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                target = "Nowy Folder";
                FolderNameInput = target;
            }
            RenameOrEnsureFolderInProfile("", target);
            NavigationRequestedCallback?.Invoke(this, DeckActionType.OpenFolder, target);
        }
    }

    [RelayCommand]
    public void TriggerGoBack()
    {
        NavigationRequestedCallback?.Invoke(this, DeckActionType.GoBack, null);
    }

    [RelayCommand]
    public void TriggerGoHome()
    {
        NavigationRequestedCallback?.Invoke(this, DeckActionType.GoHome, null);
    }

    [RelayCommand]
    public void DeleteSelectedFolder()
    {
        string target = !string.IsNullOrWhiteSpace(FolderNameInput)
            ? FolderNameInput.Trim()
            : (!string.IsNullOrWhiteSpace(SelectedFolder) ? SelectedFolder.Trim() : ActionParameter.Trim());

        if (string.IsNullOrWhiteSpace(target)) return;

        FolderDeletedCallback?.Invoke(target);

        ActionParameter = "";
        CustomTitle = "";
        OnPropertyChanged(nameof(FolderNameInput));
        OnPropertyChanged(nameof(AvailableFolders));
        OnPropertyChanged(nameof(SelectedFolder));
        ActionChangedCallback?.Invoke(this);
    }

    private void RenameOrEnsureFolderInProfile(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        var profileService = GetService<IProfileService>();
        var activeProf = profileService?.GetActiveProfile();
        if (activeProf != null)
        {
            activeProf.Folders ??= new List<KeyFolderModel>();
            var existingByOld = !string.IsNullOrWhiteSpace(oldName)
                ? activeProf.Folders.FirstOrDefault(f => f.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                : null;

            if (existingByOld != null)
            {
                existingByOld.Name = newName;
            }
            else
            {
                var existingByNew = activeProf.Folders.FirstOrDefault(f => f.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
                if (existingByNew == null)
                {
                    activeProf.Folders.Add(KeyFolderModel.Create(newName));
                }
            }
            profileService?.SaveProfile(activeProf);
            OnPropertyChanged(nameof(AvailableFolders));
        }
    }

    private void EnsureFolderExistsInProfile(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return;
        var profileService = GetService<IProfileService>();
        var activeProf = profileService?.GetActiveProfile();
        if (activeProf != null)
        {
            activeProf.Folders ??= new List<KeyFolderModel>();
            if (!activeProf.Folders.Any(f => f.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
            {
                activeProf.Folders.Add(KeyFolderModel.Create(folderName));
                profileService?.SaveProfile(activeProf);
                OnPropertyChanged(nameof(AvailableFolders));
            }
        }
    }
}

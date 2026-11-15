using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{

    public KeyItemViewModel? SelectedKey => SelectedElement as KeyItemViewModel;
    public SliderItemViewModel? SelectedSlider => SelectedElement as SliderItemViewModel;
    public RollerItemViewModel? SelectedRoller => SelectedElement as RollerItemViewModel;
    public KnobItemViewModel? SelectedKnob => SelectedElement as KnobItemViewModel;
    public DialItemViewModel? SelectedDial => SelectedElement as DialItemViewModel;
    public BaseStationItemViewModel? SelectedBase => SelectedElement as BaseStationItemViewModel;

    private int _currentPageIndex = 0;
    private int _pageRenderVersion = 0;
    private string? _currentFolderId = null;
    private string _currentFolderName = "";
    private readonly Stack<(int PageIndex, string? FolderId, string FolderName)> _navStack = new();

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        private set
        {
            if (SetProperty(ref _currentPageIndex, value))
            {
                NotifyNavProperties();
            }
        }
    }

    public string? CurrentFolderId
    {
        get => _currentFolderId;
        private set
        {
            if (SetProperty(ref _currentFolderId, value))
            {
                NotifyNavProperties();
            }
        }
    }

    public string CurrentFolderName
    {
        get => _currentFolderName;
        private set
        {
            if (SetProperty(ref _currentFolderName, value))
            {
                NotifyNavProperties();
            }
        }
    }

    public bool IsInsideFolder => !string.IsNullOrEmpty(CurrentFolderId);

    public int TotalPagesCount
    {
        get
        {
            var p = _profileService.GetActiveProfile();
            return p?.Pages != null && p.Pages.Count > 0 ? p.Pages.Count : 1;
        }
    }

    public bool CanGoPreviousPage => !IsInsideFolder && TotalPagesCount > 1;
    public bool CanGoNextPage => !IsInsideFolder && TotalPagesCount > 1;
    public bool CanDeleteCurrentPage => !IsInsideFolder && TotalPagesCount > 1;

    public string CurrentPageDisplayName
    {
        get
        {
            if (IsInsideFolder)
            {
                return $"📁 {CurrentFolderName}";
            }
            return string.Format(LocalizationService.Instance.GetString("Nav_PageOf", "Strona {0} z {1}"), CurrentPageIndex + 1, Math.Max(1, TotalPagesCount));
        }
    }

    public void NotifyNavProperties()
    {
        OnPropertyChanged(nameof(CurrentPageIndex));
        OnPropertyChanged(nameof(CurrentFolderId));
        OnPropertyChanged(nameof(CurrentFolderName));
        OnPropertyChanged(nameof(IsInsideFolder));
        OnPropertyChanged(nameof(TotalPagesCount));
        OnPropertyChanged(nameof(CurrentPageDisplayName));
        OnPropertyChanged(nameof(CanGoPreviousPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(CanDeleteCurrentPage));
    }

    [RelayCommand]
    public void NextPage()
    {
        if (IsInsideFolder) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;
        EnsurePagesInitialized(prof);
        if (prof.Pages.Count <= 1) return;

        _animatedKeyRenderer.StopAllAnimations();
        DeselectAll();
        SaveCurrentStateToActiveProfile();
        if (CurrentPageIndex < prof.Pages.Count - 1)
        {
            CurrentPageIndex++;
        }
        else
        {
            CurrentPageIndex = 0;
        }
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public void PreviousPage()
    {
        if (IsInsideFolder) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;
        EnsurePagesInitialized(prof);
        if (prof.Pages.Count <= 1) return;

        _animatedKeyRenderer.StopAllAnimations();
        DeselectAll();
        SaveCurrentStateToActiveProfile();
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
        }
        else
        {
            CurrentPageIndex = prof.Pages.Count - 1;
        }
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public void AddNewPage()
    {
        if (IsInsideFolder) return;

        // Save first and read the profile afterwards: the profile service hands out a fresh copy read from disk on every call,
        // so a copy taken before the save would not have the keys just saved and writing it back would drop them.
        DeselectAll();
        SaveCurrentStateToActiveProfile();

        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;
        EnsurePagesInitialized(prof);

        int newIdx = prof.Pages.Count;
        var newPage = KeyPageModel.Create(newIdx, $"Strona {newIdx + 1}");
        prof.Pages.Add(newPage);
        _profileService.SaveProfile(prof);

        CurrentPageIndex = newIdx;
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public async System.Threading.Tasks.Task DeleteCurrentPageAsync()
    {
        if (IsInsideFolder) return;
        if (!await _confirmDialog.ConfirmAsync(MasterHub.Avalonia.Services.ConfirmRequests.DeletePage())) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null || prof.Pages == null || prof.Pages.Count <= 1) return;

        DeselectAll();
        int toRemove = CurrentPageIndex;
        prof.Pages.RemoveAt(toRemove);
        for (int i = 0; i < prof.Pages.Count; i++)
        {
            prof.Pages[i].PageIndex = i;
            if (prof.Pages[i].Name.StartsWith("Strona "))
            {
                prof.Pages[i].Name = $"Strona {i + 1}";
            }
        }

        CurrentPageIndex = Math.Clamp(toRemove > 0 ? toRemove - 1 : 0, 0, prof.Pages.Count - 1);
        _profileService.SaveProfile(prof);
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public void NavigateBack()
    {
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;

        DeselectAll();
        if (_navStack.Count > 0)
        {
            SaveCurrentStateToActiveProfile();
            var prev = _navStack.Pop();
            CurrentPageIndex = prev.PageIndex;
            CurrentFolderId = prev.FolderId;
            CurrentFolderName = prev.FolderName;
            LoadCurrentViewKeys(prof);
            NotifyNavProperties();
        }
        else if (IsInsideFolder)
        {
            NavigateHome();
        }
        else if (CurrentPageIndex > 0)
        {
            PreviousPage();
        }
    }

    [RelayCommand]
    public void NavigateHome()
    {
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;

        DeselectAll();
        SaveCurrentStateToActiveProfile();
        _navStack.Clear();
        CurrentFolderId = null;
        CurrentFolderName = "";
        CurrentPageIndex = 0;
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    public void SwitchToPage(int pageIndex)
    {
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;
        EnsurePagesInitialized(prof);

        if (pageIndex < 0 || pageIndex >= prof.Pages.Count) return;

        _animatedKeyRenderer.StopAllAnimations();
        DeselectAll();
        SaveCurrentStateToActiveProfile();
        _navStack.Push((CurrentPageIndex, CurrentFolderId, CurrentFolderName));
        CurrentFolderId = null;
        CurrentFolderName = "";
        CurrentPageIndex = pageIndex;
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    public void OpenFolder(string folderNameOrId)
    {
        if (string.IsNullOrWhiteSpace(folderNameOrId))
        {
            folderNameOrId = "Nowy Folder";
        }
        var prof = _profileService.GetActiveProfile();
        if (prof == null) return;

        prof.Folders ??= new List<KeyFolderModel>();
        var folder = prof.Folders.FirstOrDefault(f =>
            f.Id.Equals(folderNameOrId, StringComparison.OrdinalIgnoreCase) ||
            f.Name.Equals(folderNameOrId, StringComparison.OrdinalIgnoreCase));

        if (folder == null)
        {
            folder = KeyFolderModel.Create(folderNameOrId, CurrentFolderId);
            prof.Folders.Add(folder);
            _profileService.SaveProfile(prof);
        }

        EnsureFolderKeysInitialized(folder);

        DeselectAll();
        SaveCurrentStateToActiveProfile();
        _navStack.Push((CurrentPageIndex, CurrentFolderId, CurrentFolderName));
        CurrentFolderId = folder.Id;
        CurrentFolderName = folder.Name;
        LoadCurrentViewKeys(prof);
        NotifyNavProperties();
    }

    [RelayCommand]
    public async System.Threading.Tasks.Task DeleteCurrentFolderAsync()
    {
        if (!IsInsideFolder || string.IsNullOrEmpty(CurrentFolderId)) return;
        if (!await _confirmDialog.ConfirmAsync(MasterHub.Avalonia.Services.ConfirmRequests.DeleteFolder())) return;
        DeleteFolder(CurrentFolderId);
    }

    public void DeleteFolder(string folderNameOrId)
    {
        if (string.IsNullOrWhiteSpace(folderNameOrId)) return;
        var prof = _profileService.GetActiveProfile();
        if (prof == null || prof.Folders == null) return;

        var folder = prof.Folders.FirstOrDefault(f =>
            f.Id.Equals(folderNameOrId, StringComparison.OrdinalIgnoreCase) ||
            f.Name.Equals(folderNameOrId, StringComparison.OrdinalIgnoreCase));

        if (folder == null) return;

        string targetId = folder.Id;
        string targetName = folder.Name;

        prof.Folders.RemoveAll(f => f.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));

        if (prof.Pages != null)
        {
            foreach (var page in prof.Pages)
            {
                if (page.Keys != null)
                {
                    foreach (var k in page.Keys)
                    {
                        if (k.Action == DeckActionType.OpenFolder &&
                            (k.ActionParameter.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                             k.ActionParameter.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
                        {
                            k.Action = DeckActionType.None;
                            k.ActionParameter = "";
                            k.CustomTitle = "";
                        }
                    }
                }
            }
        }

        if (prof.Keys != null)
        {
            foreach (var k in prof.Keys)
            {
                if (k.Action == DeckActionType.OpenFolder &&
                    (k.ActionParameter.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                     k.ActionParameter.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
                {
                    k.Action = DeckActionType.None;
                    k.ActionParameter = "";
                    k.CustomTitle = "";
                }
            }
        }

        _profileService.SaveProfile(prof);
        DeselectAll();

        if (IsInsideFolder && (CurrentFolderId == targetId || CurrentFolderName.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
        {
            NavigateHome();
        }
        else
        {
            LoadCurrentViewKeys(prof);
            NotifyNavProperties();
        }
    }

    public int ParsePageIndex(string? param)
    {
        if (string.IsNullOrWhiteSpace(param)) return 0;
        var prof = _profileService.GetActiveProfile();
        if (prof?.Pages != null)
        {
            int nameIdx = prof.Pages.FindIndex(p => p.Name.Equals(param.Trim(), StringComparison.OrdinalIgnoreCase));
            if (nameIdx >= 0) return nameIdx;
        }

        var match = System.Text.RegularExpressions.Regex.Match(param, @"\d+");
        if (match.Success && int.TryParse(match.Value, out int pageNum) && pageNum > 0)
        {
            return pageNum - 1;
        }

        return 0;
    }

    private static void EnsurePagesInitialized(ProfileModel prof)
    {
        if (prof.Pages == null || prof.Pages.Count == 0)
        {
            prof.Pages = new List<KeyPageModel>
            {
                new KeyPageModel
                {
                    PageIndex = 0,
                    Name = "Strona 1",
                    Keys = new List<KeyAssignmentModel>(prof.Keys)
                }
            };
        }
    }

    public static void EnsureFolderKeysInitialized(KeyFolderModel folder)
    {
        folder.Keys ??= new List<KeyAssignmentModel>();
        if (folder.Keys.Count == 0 || !folder.Keys.Any(k => k.Index == 0))
        {
            folder.Keys.RemoveAll(k => k.Index == 0);
            folder.Keys.Insert(0, new KeyAssignmentModel
            {
                Index = 0,
                Action = DeckActionType.GoBack,
                CustomTitle = "POWRÓT",
                BackgroundColorHex = "#1E293B",
                TextColorHex = "#FFFFFF"
            });
        }
        for (int i = 1; i < 15; i++)
        {
            if (!folder.Keys.Any(k => k.Index == i))
            {
                folder.Keys.Add(new KeyAssignmentModel { Index = i, Action = DeckActionType.None });
            }
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Check;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>One "check this" line of the profile check.</summary>
public sealed record CheckFindingItem(string Where, string What, string Target);

/// <summary>
/// The profile check (T-34 #9): after an import, and on demand with "Check", lists the controls that point at something this
/// PC lacks (a program, file, picture, sound, audio device, OBS scene, Home Assistant entity, plugin, or a bare name that
/// opens nothing). Nothing is changed; the list only says what to fix.
/// </summary>
public partial class ProfilesPageViewModel
{
    /// <summary>For tests: replaces the lookups built from the running services.</summary>
    internal Func<Task<IProfileCheckEnvironment>>? CheckEnvironmentFactory { get; set; }

    public ObservableCollection<CheckFindingItem> CheckFindings { get; } = new();

    [ObservableProperty]
    private string _checkTitle = "";

    [ObservableProperty]
    private bool _isCheckVisible;

    public bool HasCheckFindings => CheckFindings.Count > 0;

    [RelayCommand]
    private async Task CheckProfile(ProfileItemViewModel? item)
    {
        if (item == null) return;
        await RunCheckAsync(item.Profile);
    }

    [RelayCommand]
    private void CloseCheck() => IsCheckVisible = false;

    /// <summary>Checks a profile and shows the result card; returns how many marks it found.</summary>
    internal async Task<int> RunCheckAsync(ProfileModel profile)
    {
        var loc = LocalizationService.Instance;
        System.Collections.Generic.IReadOnlyList<CheckFinding> findings;
        try
        {
            var env = CheckEnvironmentFactory != null
                ? await CheckEnvironmentFactory()
                : await ProfileCheckEnvironments.FromServicesAsync(App.Services);
            findings = ProfileCheck.Run(profile, env);
        }
        catch
        {
            findings = Array.Empty<CheckFinding>();
        }

        CheckFindings.Clear();
        foreach (var f in findings) CheckFindings.Add(ToItem(f, loc));
        CheckTitle = findings.Count == 0
            ? string.Format(loc.GetString("Check_AllFound", "\"{0}\": everything this profile points at is on this PC."), profile.Name)
            : string.Format(loc.GetString("Check_Found", "\"{0}\": {1} control(s) point at something this PC lacks – check these:"), profile.Name, findings.Count);
        IsCheckVisible = true;
        OnPropertyChanged(nameof(HasCheckFindings));
        return findings.Count;
    }

    private static CheckFindingItem ToItem(CheckFinding f, LocalizationService loc)
    {
        string where = f.Key > 0
            ? string.Format(loc.GetString("Check_WhereKey", "Page {0}, key {1}"), f.Page, f.Key)
            : string.Format(loc.GetString("Check_WherePage", "Page {0}, wallpaper"), f.Page);
        if (f.Gesture == "hold") where += " · " + loc.GetString("Gesture_Hold", "Hold");
        else if (f.Gesture == "double") where += " · " + loc.GetString("Gesture_Double", "Double");

        return new CheckFindingItem(where, Describe(f.Problem, loc), f.Target);
    }

    private static string Describe(CheckProblem problem, LocalizationService loc) => problem switch
    {
        CheckProblem.MissingProgram => loc.GetString("Check_MissingProgram", "Program not found on this PC"),
        CheckProblem.MissingFile => loc.GetString("Check_MissingFile", "File or folder not found"),
        CheckProblem.MissingPicture => loc.GetString("Check_MissingPicture", "Picture not found"),
        CheckProblem.MissingSound => loc.GetString("Check_MissingSound", "Sound file not found"),
        CheckProblem.MissingAudioDevice => loc.GetString("Check_MissingAudioDevice", "Audio device not connected"),
        CheckProblem.MissingObsScene => loc.GetString("Check_MissingObsScene", "OBS has no scene with this name"),
        CheckProblem.UnknownHaEntity => loc.GetString("Check_UnknownHaEntity", "Home Assistant does not know this entity"),
        CheckProblem.MissingPlugin => loc.GetString("Check_MissingPlugin", "Stream Deck plugin not installed"),
        CheckProblem.BareNameNotFound => loc.GetString("Check_BareNameNotFound", "This name opens nothing here"),
        _ => problem.ToString()
    };
}

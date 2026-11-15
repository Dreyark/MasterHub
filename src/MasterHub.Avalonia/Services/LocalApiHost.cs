using System.Linq;
using Avalonia.Threading;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Core.Api;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// The desktop app's side of the local API: everything that must run on the UI thread or needs the Studio's in-memory
/// state (activating a profile, saving the Studio's edits before an API write, re-applying a changed profile).
/// </summary>
public sealed class LocalApiHost : ILocalApiHost
{
    private readonly ProfilesPageViewModel _profilesPage;
    private readonly CanvasViewModel _canvas;
    private readonly IProfileService _profiles;

    public LocalApiHost(ProfilesPageViewModel profilesPage, CanvasViewModel canvas, IProfileService profiles)
    {
        _profilesPage = profilesPage;
        _canvas = canvas;
        _profiles = profiles;
    }

    public bool ActivateProfile(string profileId) => OnUi(() =>
    {
        var item = _profilesPage.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (item == null)
        {
            _profilesPage.RefreshProfiles();
            item = _profilesPage.Profiles.FirstOrDefault(p => p.Id == profileId);
        }
        if (item == null) return false;

        _profilesPage.ActivateProfileCommand.Execute(item);
        return true;
    });

    public void FlushActiveProfile() => OnUi(() =>
    {
        _canvas.SaveCurrentStateToActiveProfile();
        return true;
    });

    public void ProfileChanged(string profileId) => OnUi(() =>
    {
        _profilesPage.RefreshProfiles();
        if (_profiles.GetActiveProfile().Id == profileId)
        {
            _canvas.ApplyProfile(_profiles.GetActiveProfile(), saveCurrent: false);
        }
        return true;
    });

    public bool SetLights(LightsMode mode, string? colorHex) => OnUi(() => _canvas.ApplyLightsOverride(mode, colorHex));

    private static T OnUi<T>(System.Func<T> work) =>
        Dispatcher.UIThread.CheckAccess() ? work() : Dispatcher.UIThread.Invoke(work);
}

using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>Moving between pages must never lose the keys that were just edited.</summary>
public sealed class PageNavigationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_nav_ui_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private (CanvasViewModel Canvas, ProfileService Profiles) CreateWithProfiles()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        return (canvas, profiles);
    }

    private CanvasViewModel Create() => CreateWithProfiles().Canvas;

    [AvaloniaFact]
    public void AddingAPage_RightAfterAnEdit_KeepsTheEditedKeys()
    {
        var canvas = Create();
        canvas.Keys[3].AssignedAction = DeckActionType.MediaPlayPause;
        canvas.Keys[3].CustomTitle = "Edited";

        canvas.AddNewPage();                       // no pause for the auto-save in between
        Assert.Equal(DeckActionType.None, canvas.Keys[3].AssignedAction);   // the new page is empty

        canvas.SwitchToPage(0);

        Assert.Equal(DeckActionType.MediaPlayPause, canvas.Keys[3].AssignedAction);
        Assert.Equal("Edited", canvas.Keys[3].CustomTitle);
    }

    private static ProfileModel Saved(ProfileService profiles, string id) => profiles.GetAllProfiles().First(p => p.Id == id);

    /// <summary>What happens on a click on the Profiles page: the active profile changes first, then the canvas is given the new one.</summary>
    [AvaloniaFact]
    public void SwitchingProfile_SavesTheCanvasIntoThePreviousProfile_AndNeverIntoTheNewOne()
    {
        var (canvas, profiles) = CreateWithProfiles();
        var a = profiles.GetActiveProfile();
        canvas.Keys[0].AssignedAction = DeckActionType.MicMute;
        canvas.SaveCurrentStateToActiveProfile();

        var b = profiles.CreateProfile("B", setAsActive: false);
        var bModel = Saved(profiles, b.Id);
        bModel.Keys = new List<KeyAssignmentModel> { new() { Index = 1, Action = DeckActionType.MediaNext } };
        bModel.Pages[0].Keys = new List<KeyAssignmentModel>(bModel.Keys);
        profiles.SaveProfile(bModel);

        canvas.Keys[2].AssignedAction = DeckActionType.MasterMute;   // an edit of A that was not saved yet
        profiles.SetActiveProfile(b.Id);
        canvas.ApplyProfile(Saved(profiles, b.Id));

        // B kept its own keys on disk (before the fix it received the keys of A)
        var bKeys = Saved(profiles, b.Id).Pages[0].Keys.Where(k => k.Action != DeckActionType.None).ToList();
        Assert.Equal(new[] { DeckActionType.MediaNext }, bKeys.Select(k => k.Action).ToArray());

        // and the last edit of A was saved into A
        var aKeys = Saved(profiles, a.Id).Pages[0].Keys.Where(k => k.Action != DeckActionType.None).Select(k => k.Action).ToList();
        Assert.Contains(DeckActionType.MicMute, aKeys);
        Assert.Contains(DeckActionType.MasterMute, aKeys);

        Assert.Equal(DeckActionType.MediaNext, canvas.Keys[1].AssignedAction);
        Assert.Equal(DeckActionType.None, canvas.Keys[0].AssignedAction);
    }

    [AvaloniaFact]
    public void SwitchingAwayFromADeletedProfile_DoesNotCrashAndDoesNotWriteAnywhereElse()
    {
        var (canvas, profiles) = CreateWithProfiles();
        var other = profiles.CreateProfile("Other", setAsActive: false);
        var otherModel = Saved(profiles, other.Id);
        otherModel.Keys = new List<KeyAssignmentModel> { new() { Index = 4, Action = DeckActionType.MediaStop } };
        otherModel.Pages[0].Keys = new List<KeyAssignmentModel>(otherModel.Keys);
        profiles.SaveProfile(otherModel);

        var first = profiles.GetActiveProfile();
        canvas.Keys[0].AssignedAction = DeckActionType.MicMute;
        profiles.SetActiveProfile(other.Id);
        profiles.DeleteProfile(first.Id);                          // the profile on the canvas is gone

        canvas.ApplyProfile(Saved(profiles, other.Id));

        Assert.Equal(new[] { DeckActionType.MediaStop },
            Saved(profiles, other.Id).Pages[0].Keys.Where(k => k.Action != DeckActionType.None).Select(k => k.Action).ToArray());
    }
}

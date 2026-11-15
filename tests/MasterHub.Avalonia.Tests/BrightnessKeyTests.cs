using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.ViewModels.Inspector;
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
using MasterHub.Core.Localization;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>T-17 (a): a key moves the deck's brightness – the same setting as the slider.</summary>
public sealed class BrightnessKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_bright_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private (CanvasViewModel Canvas, RecordingProxy<IMasterHubDeviceService> Deck) Create()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var (deck, log) = RecordingProxy<IMasterHubDeviceService>.Create();
        var canvas = new CanvasViewModel(deck, F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        canvas.KeypadBrightness = 60;
        log.Clear();
        return (canvas, log);
    }

    private static void Press(CanvasViewModel canvas, DeckActionType action, string parameter = "")
    {
        var key = canvas.Keys[0];
        key.AssignedAction = action;
        key.ActionParameter = parameter;
        canvas.ExecuteKeyAction(key);
    }

    [AvaloniaFact]
    public void Up_AndDown_MoveTheSliderAndTheScreen()
    {
        var (canvas, deck) = Create();

        Press(canvas, DeckActionType.DeckBrightnessUp);
        Assert.Equal(70, canvas.KeypadBrightness);
        Assert.Contains(deck.Calls, c => c.Name == "SetKeypadBacklight" && (byte)c.Args[0]! == 70);

        Press(canvas, DeckActionType.DeckBrightnessDown, "25");
        Assert.Equal(45, canvas.KeypadBrightness);
    }

    [AvaloniaFact]
    public void Down_NeverBlacksTheScreenOut()
    {
        var (canvas, _) = Create();
        canvas.KeypadBrightness = 4;

        Press(canvas, DeckActionType.DeckBrightnessDown);
        Press(canvas, DeckActionType.DeckBrightnessDown);

        Assert.Equal(1, canvas.KeypadBrightness);
    }

    [AvaloniaFact]
    public void Cycle_GoesThroughTheLevels()
    {
        var (canvas, _) = Create();
        canvas.KeypadBrightness = 100;

        var seen = new List<int>();
        for (int i = 0; i < 5; i++) { Press(canvas, DeckActionType.DeckBrightnessCycle); seen.Add(canvas.KeypadBrightness); }

        Assert.Equal(new[] { 75, 50, 25, 10, 100 }, seen);
    }

    [AvaloniaFact]
    public void TheActionsHaveANameAndALibraryEntry()
    {
        var library = new ActionLibraryViewModel();
        foreach (var action in new[] { DeckActionType.DeckBrightnessUp, DeckActionType.DeckBrightnessDown, DeckActionType.DeckBrightnessCycle })
        {
            Assert.NotEmpty(LocalizationService.Instance.GetString($"Action_{action}", ""));
            Assert.Contains(library.FilteredItems, a => a.KeyAction == action);
        }
    }
}

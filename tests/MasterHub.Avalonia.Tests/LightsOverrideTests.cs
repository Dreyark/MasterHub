using System.Reflection;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Api;
using MasterHub.Core.Audio;
using MasterHub.Core.Devices;
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

/// <summary>A stand-in that remembers every call with its arguments (the other proxies only count).</summary>
public class RecordingProxy<T> : DispatchProxy where T : class
{
    public List<(string Name, object?[] Args)> Calls { get; } = new();

    public static (T Service, RecordingProxy<T> Log) Create()
    {
        var service = DispatchProxy.Create<T, RecordingProxy<T>>();
        return (service, (RecordingProxy<T>)(object)service);
    }

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        Calls.Add((m!.Name, args ?? Array.Empty<object?>()));
        var type = m.ReturnType;
        if (type == typeof(void)) return null;
        if (type == typeof(string)) return "";
        if (type == typeof(Task)) return Task.CompletedTask;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    public int Count(string name) => Calls.Count(c => c.Name == name);
    public void Clear() => Calls.Clear();
}

/// <summary>T-24: the lights the CLI sets last until the profile changes, and never touch the profile itself.</summary>
public sealed class LightsOverrideTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_lights_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private (CanvasViewModel Canvas, RecordingProxy<IMasterHubDeviceService> Deck, ProfileService Profiles) Create()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var (deck, log) = RecordingProxy<IMasterHubDeviceService>.Create();
        var canvas = new CanvasViewModel(deck, F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        log.Clear();
        return (canvas, log, profiles);
    }

    [AvaloniaFact]
    public void Rainbow_GoesToTheBase_TheFaders_AndTheKnobs_WithoutSendingColours()
    {
        var (canvas, deck, _) = Create();

        Assert.True(canvas.ApplyLightsOverride(LightsMode.Rainbow, null));

        Assert.Equal(1, deck.Count("SetBaseLedMode"));
        Assert.Equal(LedEffect.Rainbow, deck.Calls.Single(c => c.Name == "SetBaseLedMode").Args[0]);
        Assert.Equal(canvas.Sliders.Count, deck.Count("SetSliderLedMode"));
        Assert.All(deck.Calls.Where(c => c.Name == "SetSliderLedMode"), c => Assert.Equal(LedEffect.Rainbow, c.Args[1]));
        Assert.Equal(canvas.Knobs.Count, deck.Count("SetKnobLedMode"));
        Assert.All(deck.Calls.Where(c => c.Name == "SetKnobLedMode"), c => Assert.Equal(KnobLedMode.Rainbow, c.Args[1]));
        Assert.Equal(0, deck.Count("SetBaseLedColor"));
    }

    [AvaloniaFact]
    public void AColour_IsSentToEveryLight_AndTheKnobsGoBackToStatic()
    {
        var (canvas, deck, _) = Create();

        Assert.True(canvas.ApplyLightsOverride(LightsMode.Color, "#ff8800"));

        var baseColour = deck.Calls.Single(c => c.Name == "SetBaseLedColor").Args;
        Assert.True((byte)baseColour[0]! > 0);   // red is on
        Assert.Equal(canvas.Sliders.Count, deck.Count("SetSliderLedColor"));
        Assert.Equal(canvas.Knobs.Count, deck.Count("SetKnobLedColor"));
        Assert.All(deck.Calls.Where(c => c.Name == "SetKnobLedMode"), c => Assert.Equal(KnobLedMode.Static, c.Args[1]));
    }

    [AvaloniaFact]
    public void Off_IsBlackEverywhere()
    {
        var (canvas, deck, _) = Create();

        Assert.True(canvas.ApplyLightsOverride(LightsMode.Off, null));

        Assert.All(deck.Calls.Where(c => c.Name is "SetBaseLedColor" or "SetSliderLedColor" or "SetKnobLedColor" or "SetRollerLedColor"),
            c => Assert.All(c.Args.Skip(c.Name == "SetBaseLedColor" ? 0 : 1), a => Assert.Equal((byte)0, a)));
        Assert.Equal(1, deck.Count("SetBaseLedColor"));
    }

    [AvaloniaFact]
    public void ABadColour_IsRefused_AndNothingIsSent()
    {
        var (canvas, deck, _) = Create();

        Assert.False(canvas.ApplyLightsOverride(LightsMode.Color, "not a colour"));

        Assert.Empty(deck.Calls);
    }

    [AvaloniaFact]
    public void TheLights_NeverTouchTheProfile()
    {
        var (canvas, _, profiles) = Create();
        string before = System.Text.Json.JsonSerializer.Serialize(profiles.GetActiveProfile());

        canvas.ApplyLightsOverride(LightsMode.Color, "#00ff00");
        canvas.ApplyLightsOverride(LightsMode.Rainbow, null);
        canvas.ApplyLightsOverride(LightsMode.Off, null);

        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(profiles.GetActiveProfile()));
    }

    [AvaloniaFact]
    public void WhileTheLightsAreSet_TheProfilesEdgeColourDoesNotOverwriteThem()
    {
        var (canvas, deck, _) = Create();
        canvas.ApplyLightsOverride(LightsMode.Rainbow, null);
        deck.Clear();

        canvas.BaseStation.ColorHex = "#112233";   // an edit of the profile edge colour

        Assert.Equal(0, deck.Count("SetBaseLedColor"));
    }

    [AvaloniaFact]
    public void AnotherProfile_GivesTheLightsBackToTheProfile()
    {
        var (canvas, deck, profiles) = Create();
        canvas.ApplyLightsOverride(LightsMode.Rainbow, null);
        deck.Clear();

        canvas.ApplyProfile(profiles.GetActiveProfile(), saveCurrent: false);

        Assert.True(deck.Count("SetBaseLedColor") > 0);   // the edge colour of the profile is sent again
    }

    [AvaloniaFact]
    public void ProfileMode_GivesTheLightsBack_WithoutChangingProfile()
    {
        var (canvas, deck, profiles) = Create();
        string id = profiles.GetActiveProfile().Id;
        canvas.ApplyLightsOverride(LightsMode.Off, null);
        deck.Clear();

        Assert.True(canvas.ApplyLightsOverride(LightsMode.Profile, null));

        Assert.True(deck.Count("SetBaseLedColor") > 0);
        Assert.Equal(id, profiles.GetActiveProfile().Id);

        // and the override is gone: edits of the edge colour reach the base again
        deck.Clear();
        canvas.BaseStation.ColorHex = "#334455";
        Assert.True(deck.Count("SetBaseLedColor") > 0);
    }
}

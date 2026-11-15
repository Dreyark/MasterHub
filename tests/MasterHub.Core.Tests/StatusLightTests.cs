using MasterHub.Core.Audio;
using MasterHub.Core.Devices;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Tests;

/// <summary>T-18: the fixed meaning of the edge-light colours (priority stack) and the service that watches the sources.</summary>
public class StatusLightTests
{
    private static StatusLightSettings On() => new() { Enabled = true };

    [Fact]
    public void Disabled_ShowsTheProfilesOwnLight()
    {
        var all = new StatusInputs(true, true, true, true, true);
        Assert.Null(StatusLightResolver.Resolve(all, new StatusLightSettings { Enabled = false }));
    }

    [Fact]
    public void Nothing_Happening_ShowsTheProfilesOwnLight()
    {
        Assert.Null(StatusLightResolver.Resolve(default, On()));
    }

    [Fact]
    public void PriorityStack_AttentionBeatsLive_LiveBeatsReady()
    {
        var all = new StatusInputs(Attention: true, MicMuted: true, ObsLive: true, DiscordMuted: true, Ready: true);

        var attention = StatusLightResolver.Resolve(all, On())!.Value;
        Assert.Equal((255, 140, 0), (attention.R, attention.G, attention.B));
        Assert.Equal(LedEffect.Breathing, attention.Effect);   // orange pulse

        var live = StatusLightResolver.Resolve(all with { Attention = false }, On())!.Value;
        Assert.Equal((255, 0, 0), (live.R, live.G, live.B));
        Assert.Equal(LedEffect.Static, live.Effect);           // red, calm

        var ready = StatusLightResolver.Resolve(new StatusInputs(false, false, false, false, true), On())!.Value;
        Assert.Equal((0, 200, 80), (ready.R, ready.G, ready.B)); // green
    }

    [Theory]
    [InlineData(true, false, false, "microphone muted")]
    [InlineData(false, true, false, "OBS is live")]
    [InlineData(false, false, true, "Discord muted")]
    public void EachLiveSource_TurnsTheEdgeRed_WithItsReason(bool mic, bool obs, bool discord, string reason)
    {
        var light = StatusLightResolver.Resolve(new StatusInputs(false, mic, obs, discord, false), On())!.Value;
        Assert.Equal(255, light.R);
        Assert.Equal(reason, light.Reason);
    }

    [Fact]
    public void EveryReaction_CanBeSwitchedOffOnItsOwn()
    {
        var micOnly = new StatusInputs(false, true, false, false, false);
        var s = On();
        Assert.NotNull(StatusLightResolver.Resolve(micOnly, s));

        s.MicMuted = false;
        Assert.Null(StatusLightResolver.Resolve(micOnly, s));

        // another reaction still works
        Assert.NotNull(StatusLightResolver.Resolve(new StatusInputs(false, false, true, false, false), s));
        s.ObsLive = false;
        Assert.Null(StatusLightResolver.Resolve(new StatusInputs(false, false, true, false, false), s));

        s.Attention = false;
        Assert.Null(StatusLightResolver.Resolve(new StatusInputs(true, false, false, false, false), s));
    }

    [Fact]
    public void ProfileDefaults_AreBackwardsCompatible()
    {
        var profile = new ProfileModel();
        Assert.Equal(LedEffect.Static, profile.BaseLedMode);
        Assert.Equal(50, profile.BaseLedSpeed);
        Assert.False(new AppSettingsModel().StatusLights.Enabled);   // off by default
    }

    // ── service ────────────────────────────────────────────────────────────────────────────

    private sealed class FakeAudio : IAudioService
    {
        public bool MicMuted { get; set; }
        public bool IsMasterRecordingMuted() => MicMuted;
        public float GetMasterPlaybackVolume() => 0; public void SetMasterPlaybackVolume(float volume) { }
        public bool ToggleMasterPlaybackMute() => false; public bool IsMasterPlaybackMuted() => false;
        public float GetMasterRecordingVolume() => 0; public void SetMasterRecordingVolume(float volume) { }
        public bool ToggleMasterRecordingMute() => false;
        public List<AudioSessionItem> GetActiveAudioSessions() => new();
        public void SetProcessVolume(string processName, float volume) { }
        public bool ToggleProcessMute(string processName) => false; public bool IsProcessMuted(string processName) => false;
        public void Dispose() { }
    }

    [Fact]
    public void Service_ReportsTheLight_WhenTheMicrophoneIsMuted_AndRestoresWhenItIsNot()
    {
        var audio = new FakeAudio();
        var settings = On();
        using var service = new DeckStatusLightService(null, null, audio, () => settings);
        var changes = new List<StatusLight?>();
        service.Changed += changes.Add;

        service.Start();
        Assert.Empty(changes);                         // nothing muted: nothing to say

        audio.MicMuted = true;
        service.Refresh();
        Assert.Equal(255, changes.Single()!.Value.R);

        service.Refresh();                              // no change: no second event
        Assert.Single(changes);

        audio.MicMuted = false;
        service.Refresh();
        Assert.Null(changes.Last());                    // back to the profile's own edge light
        Assert.Null(service.Current);
    }

    [Fact]
    public void Service_SwitchedOff_ReportsNothing_AndAttentionPulses()
    {
        var audio = new FakeAudio { MicMuted = true };
        var settings = new StatusLightSettings { Enabled = false };
        using var service = new DeckStatusLightService(null, null, audio, () => settings);
        var changes = new List<StatusLight?>();
        service.Changed += changes.Add;

        service.Start();
        Assert.Empty(changes);

        settings.Enabled = true;
        service.Attention = true;
        Assert.Equal(LedEffect.Breathing, changes.Last()!.Value.Effect);
    }
}

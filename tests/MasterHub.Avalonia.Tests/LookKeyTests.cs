using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.Views.Inspector;
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
using MasterHub.Core.Looks;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>An interface whose property getters answer from a table and whose calls are counted (for the services the look signals read).</summary>
public class ScriptedProxy<T> : DispatchProxy where T : class
{
    public Dictionary<string, object?> Values { get; } = new();
    public Dictionary<string, int> Calls { get; } = new();

    public static (T Service, ScriptedProxy<T> Script) Create()
    {
        var service = DispatchProxy.Create<T, ScriptedProxy<T>>();
        return (service, (ScriptedProxy<T>)(object)service);
    }

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        string name = m!.Name.StartsWith("get_") ? m.Name[4..] : m.Name;
        Calls[name] = Calls.GetValueOrDefault(name) + 1;

        var type = m.ReturnType;
        if (type == typeof(void)) return null;
        if (Values.TryGetValue(name, out var value) && value != null) return Convert.ChangeType(value, type);
        if (type == typeof(string)) return "";
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

/// <summary>T-12 in the Studio: the rule editor, saving, and the evaluator on the real canvas view model (stub services).</summary>
public sealed class LookKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_look_ui_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private sealed class FixedSignals : ILookSignals
    {
        public double Cpu { get; set; }
        public double Ram { get; set; }
        public double Gpu { get; set; }
        public double CpuTemp { get; set; }
        public double GpuTemp { get; set; }
        public bool MusicPlaying { get; set; }
        public bool MicMuted { get; set; }
        public bool MasterMuted { get; set; }
        public string? ForegroundApp { get; set; }
        public string? Song { get; set; }
        public string? Artist { get; set; }
        public DateTime Now { get; set; } = new(2026, 11, 14, 12, 0, 0);
    }

    private (CanvasViewModel Canvas, ProfileService Profiles, FixedSignals Signals) Create()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        var signals = new FixedSignals();
        canvas.LookSignalSource = signals;
        canvas.ApplyProfile(profiles.GetActiveProfile());
        return (canvas, profiles, signals);
    }

    private static KeyLookRule CpuRule(double limit = 80, string? bg = "#112233", string? title = null) => new()
    {
        When = new KeyLookCondition { Kind = LookConditionKind.CpuAbove, Threshold = limit },
        BackgroundColorHex = bg,
        Title = title
    };

    // ── the editor ──────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Editor_AddsRules_EditsThem_AndNotifiesTheStudio()
    {
        var key = new KeyItemViewModel { Index = 0 };
        int saves = 0;
        key.ActionChangedCallback = _ => saves++;

        Assert.False(key.HasLookRules);
        Assert.Null(key.ToLookModel());

        key.AddLookRuleCommand.Execute(null);
        var item = key.LookRuleItems.Single();
        item.SelectedCondition = item.Conditions.First(c => c.Kind == LookConditionKind.CpuAbove);
        item.Threshold = 75;
        item.UseBackground = true;
        item.BackgroundColor = Color.Parse("#B3261E");
        item.Title = "HOT {cpu}";

        var rule = key.ToLookModel()!.Single();
        Assert.Equal(LookConditionKind.CpuAbove, rule.When.Kind);
        Assert.Equal(75, rule.When.Threshold);
        Assert.Equal("#B3261E", rule.BackgroundColorHex);
        Assert.Equal("HOT {cpu}", rule.Title);
        Assert.Null(rule.TextColorHex);                      // not ticked: the key keeps its own text colour
        Assert.True(item.ShowsThreshold);
        Assert.False(item.ShowsText);
        Assert.True(saves >= 5);

        item.UseBackground = false;
        Assert.Null(rule.BackgroundColorHex);                // un-ticked: the colour is not changed any more

        key.RemoveLookRuleCommand.Execute(item);
        Assert.False(key.HasLookRules);
        Assert.Null(key.ToLookModel());
    }

    [AvaloniaFact]
    public void ConditionKinds_ShowTheirInput()
    {
        var key = new KeyItemViewModel { Index = 0 };
        key.AddLookRuleCommand.Execute(null);
        var item = key.LookRuleItems.Single();

        item.SelectedCondition = item.Conditions.First(c => c.Kind == LookConditionKind.AppInFront);
        Assert.True(item.ShowsText);
        Assert.False(item.ShowsThreshold);

        item.SelectedCondition = item.Conditions.First(c => c.Kind == LookConditionKind.MicMuted);
        Assert.False(item.ShowsText);
        Assert.False(item.ShowsThreshold);
    }

    [AvaloniaFact]
    public void Rules_SurviveSerialisation_AndAKeyWithoutRulesStaysOldStyle()
    {
        var key = new KeyItemViewModel { Index = 0 };
        key.AddLookRuleCommand.Execute(null);
        var item = key.LookRuleItems.Single();
        item.SelectedCondition = item.Conditions.First(c => c.Kind == LookConditionKind.TimeBetween);
        item.ConditionText = "22:00-06:00";
        item.UseTextColor = true;
        item.TextColor = Color.Parse("#FFCC00");

        var model = new KeyAssignmentModel { Index = 0, LookRules = key.ToLookModel() };
        var back = JsonSerializer.Deserialize<KeyAssignmentModel>(JsonSerializer.Serialize(model))!;

        var loaded = new KeyItemViewModel { Index = 1 };
        loaded.LoadLookRules(back.LookRules);
        var rule = loaded.LookRules.Single();
        Assert.Equal(LookConditionKind.TimeBetween, rule.When.Kind);
        Assert.Equal("22:00-06:00", rule.When.Text);
        Assert.Equal("#FFCC00", rule.TextColorHex);
        Assert.True(loaded.LookRuleItems.Single().UseTextColor);

        // a model without rules loads as a plain key, and does not write the field
        var plain = JsonSerializer.Serialize(new KeyAssignmentModel { Index = 2 });
        Assert.Null(JsonSerializer.Deserialize<KeyAssignmentModel>(plain)!.LookRules);
        loaded.LoadLookRules(null);
        Assert.False(loaded.HasLookRules);
    }

    [AvaloniaFact]
    public void Editor_RendersInTheInspector()
    {
        var key = new KeyItemViewModel { Index = 0 };
        key.AddLookRuleCommand.Execute(null);
        var item = key.LookRuleItems.Single();
        item.SelectedCondition = item.Conditions.First(c => c.Kind == LookConditionKind.CpuAbove);
        item.UseBackground = true;
        item.Title = "HOT {cpu}";

        Assert.True(File.Exists(Shot.Render(new KeyLooksView { DataContext = key }, "inspector_key_looks", 340, 420)));
    }

    // ── the evaluator on the canvas ─────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Evaluate_AppliesTheRule_OnlyWhileItHolds_AndRedrawsOnlyOnChange()
    {
        var (canvas, _, signals) = Create();
        var key = canvas.Keys[2];
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.BackgroundColorHex = "#000000";
        key.LoadLookRules(new List<KeyLookRule> { CpuRule(80, "#112233") });

        signals.Cpu = 10;
        Assert.False(canvas.RefreshLook(key, signals));        // nothing holds, nothing changed
        Assert.Equal("#000000", key.RenderBackgroundHex);

        signals.Cpu = 95;
        Assert.True(canvas.RefreshLook(key, signals));          // the rule came on: redraw
        Assert.Equal("#112233", key.RenderBackgroundHex);
        Assert.False(canvas.RefreshLook(key, signals));         // the same look again: no redraw

        signals.Cpu = 20;
        Assert.True(canvas.RefreshLook(key, signals));          // below the hysteresis margin: back to normal
        Assert.Equal("#000000", key.RenderBackgroundHex);
    }

    [AvaloniaFact]
    public void Evaluate_LabelTokens_RedrawWhenTheFilledTextChanges_EvenWithoutRules()
    {
        var (canvas, _, signals) = Create();
        var key = canvas.Keys[3];
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.CustomTitle = "CPU {cpu}";

        signals.Cpu = 30;
        Assert.True(canvas.RefreshLook(key, signals));          // first fill
        Assert.False(canvas.RefreshLook(key, signals));
        signals.Cpu = 31;
        Assert.True(canvas.RefreshLook(key, signals));          // the number changed
        signals.Cpu = 31.2;
        Assert.False(canvas.RefreshLook(key, signals));         // rounds to the same text: nothing to redraw
    }

    [AvaloniaFact]
    public void Evaluate_RuleTitle_ReplacesTheLabel_AndFillsItsTokens()
    {
        var (canvas, _, signals) = Create();
        var key = canvas.Keys[4];
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.CustomTitle = "Play";
        key.LoadLookRules(new List<KeyLookRule> { CpuRule(80, null, "HOT {cpu}") });

        signals.Cpu = 91;
        canvas.EvaluateLooks();

        Assert.Equal("HOT 91%", key.AppliedLook.Title);
        Assert.Equal("Play", key.CustomTitle);                  // the saved label is never touched
    }

    [AvaloniaFact]
    public void EditingARule_TakesEffectAtOnce()
    {
        var (canvas, _, signals) = Create();
        var key = canvas.Keys[5];
        key.AssignedAction = DeckActionType.MediaPlayPause;
        signals.MicMuted = true;

        key.AddLookRuleCommand.Execute(null);
        var item = key.LookRuleItems.Single();
        item.SelectedCondition = item.Conditions.First(c => c.Kind == LookConditionKind.MicMuted);
        item.UseBackground = true;
        item.BackgroundColor = Color.Parse("#B3261E");

        Assert.Equal("#B3261E", key.RenderBackgroundHex);       // no waiting for the 500 ms tick

        item.BackgroundColor = Color.Parse("#00AA00");           // same rule holds, only its colour changed
        Assert.Equal("#00AA00", key.RenderBackgroundHex);
    }

    [AvaloniaFact]
    public void TheTimer_RunsOnlyWhileAKeyHasRulesOrTokens()
    {
        var (canvas, _, _) = Create();
        Assert.False(canvas.IsLookTimerRunning);

        var key = canvas.Keys[1];
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.AddLookRuleCommand.Execute(null);
        var item = key.LookRuleItems.Single();
        item.SelectedCondition = item.Conditions.First(c => c.Kind == LookConditionKind.MusicPlaying);
        Assert.True(canvas.IsLookTimerRunning);

        key.RemoveLookRuleCommand.Execute(item);
        Assert.False(canvas.IsLookTimerRunning);

        key.CustomTitle = "It is {time}";
        Assert.True(canvas.IsLookTimerRunning);                 // a token alone is enough
    }

    [AvaloniaFact]
    public void Rules_ArePersistedWithTheProfile()
    {
        var (canvas, profiles, _) = Create();
        var key = canvas.Keys[6];
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.LoadLookRules(new List<KeyLookRule> { CpuRule(70, "#445566") });
        canvas.SaveCurrentStateToActiveProfile();

        var saved = profiles.GetActiveProfile().Keys.Single(k => k.Index == 6);
        Assert.Equal("#445566", saved.LookRules!.Single().BackgroundColorHex);

        // another profile load puts them back on the key
        canvas.ApplyProfile(profiles.GetActiveProfile(), saveCurrent: false);
        Assert.Equal(70, canvas.Keys[6].LookRules.Single().When.Threshold);
    }

    // ── the signals ─────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void LookSignals_ReadTheServices_WithTheTemperatureFallback()
    {
        var (monitor, ms) = ScriptedProxy<ISystemMonitorService>.Create();
        ms.Values["CpuUsagePercent"] = 42;
        ms.Values["RamUsagePercent"] = 63;
        ms.Values["GpuUsagePercent"] = 7;
        ms.Values["CpuTemperatureCelsius"] = 0;      // no CPU sensor
        ms.Values["GpuTemperatureCelsius"] = 71;
        var (nowPlaying, np) = ScriptedProxy<INowPlayingService>.Create();
        np.Values["IsPlaying"] = true;
        np.Values["CurrentTitle"] = "Song";
        np.Values["CurrentArtist"] = "Band";

        var signals = new LookSignals(monitor, nowPlaying, null, () => "obs64", () => new DateTime(2026, 11, 14, 9, 30, 0));

        Assert.Equal(42, signals.Cpu);
        Assert.Equal(63, signals.Ram);
        Assert.Equal(7, signals.Gpu);
        Assert.Equal(71, signals.CpuTemp);           // the GPU sensor stands in
        Assert.True(signals.MusicPlaying);
        Assert.Equal("Band", signals.Artist);
        Assert.Equal("Song", signals.Song);
        Assert.Equal("obs64", signals.ForegroundApp);
        Assert.Equal(new DateTime(2026, 11, 14, 9, 30, 0), signals.Now);
        Assert.False(signals.MicMuted);              // no audio service: never muted
    }

    [AvaloniaFact]
    public void LookSignals_AudioIsSampledSparingly()
    {
        var (audio, script) = ScriptedProxy<IAudioService>.Create();
        script.Values["IsMasterRecordingMuted"] = true;
        script.Values["IsMasterPlaybackMuted"] = false;
        var now = new DateTime(2026, 11, 14, 12, 0, 0);

        var signals = new LookSignals(F<ISystemMonitorService>(), F<INowPlayingService>(), audio, () => null, () => now);

        Assert.True(signals.MicMuted);
        Assert.False(signals.MasterMuted);
        Assert.Equal(1, script.Calls["IsMasterRecordingMuted"]);

        now = now.AddMilliseconds(400);              // within the throttle: the same answer, no new COM call
        Assert.True(signals.MicMuted);
        Assert.Equal(1, script.Calls["IsMasterRecordingMuted"]);

        now = now.AddSeconds(2);                     // later: sampled again
        script.Values["IsMasterRecordingMuted"] = false;
        Assert.False(signals.MicMuted);
        Assert.Equal(2, script.Calls["IsMasterRecordingMuted"]);
    }
}

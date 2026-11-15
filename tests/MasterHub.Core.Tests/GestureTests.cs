using System.Text.Json;
using MasterHub.Core.Actions;
using MasterHub.Core.Api;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Tests;

/// <summary>T-05: press / hold / double on one key – the state machine with an explicit clock (no sleeping), the engine, JSON compatibility.</summary>
public class GestureTests
{
    private static readonly GestureBindings None = new(false, false);
    private static readonly GestureBindings HoldOnly = new(true, false);
    private static readonly GestureBindings DoubleOnly = new(false, true);
    private static readonly GestureBindings Both = new(true, true);

    private static string Run(GestureRecognizer r, params (long At, string Event)[] script)
    {
        // events: "down", "up", "tick"; returns the fired gestures in order, e.g. "Press,Hold"
        var fired = new List<string>();
        foreach (var (at, ev) in script)
        {
            switch (ev)
            {
                case "down": fired.AddRange(r.OnKey(1, true, CurrentBound, at).Select(g => g.ToString())); break;
                case "up": fired.AddRange(r.OnKey(1, false, CurrentBound, at).Select(g => g.ToString())); break;
                case "tick": fired.AddRange(r.Tick(at).Select(t => t.Gesture.ToString())); break;
            }
        }
        return string.Join(",", fired);
    }

    private static GestureBindings CurrentBound = None;

    private static string Run(GestureBindings bound, params (long At, string Event)[] script)
    {
        CurrentBound = bound;
        return Run(new GestureRecognizer(holdMs: 500, doubleMs: 300), script);
    }

    // ── press-only (as before) ─────────────────────────────────────────────────────────────

    [Fact]
    public void PressOnly_FiresOnTheWayDown_AndNothingElse()
    {
        Assert.Equal("Press", Run(None, (0, "down"), (80, "up"), (900, "tick")));
        Assert.Equal("Press,Press", Run(None, (0, "down"), (50, "up"), (100, "down"), (150, "up")));
    }

    [Fact]
    public void LongPress_WithoutGestures_StillFiresOnce()
    {
        Assert.Equal("Press", Run(None, (0, "down"), (600, "tick"), (3000, "up")));
    }

    // ── press + hold ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void PressPlusHold_ShortTap_FiresPressOnRelease()
    {
        Assert.Equal("", Run(HoldOnly, (0, "down")));                              // nothing yet: it could still become a hold
        Assert.Equal("Press", Run(HoldOnly, (0, "down"), (200, "up")));
    }

    [Fact]
    public void PressPlusHold_Held_FiresHoldOnce_AndNoPressOnRelease()
    {
        Assert.Equal("Hold", Run(HoldOnly, (0, "down"), (500, "tick"), (2000, "up")));
    }

    [Fact]
    public void Hold_IsNotMissed_WhenTheReleaseArrivesBeforeTheTick()
    {
        Assert.Equal("Hold", Run(HoldOnly, (0, "down"), (520, "up")));
    }

    [Fact]
    public void HoldDeadline_IsReported_ForTheTimer()
    {
        var r = new GestureRecognizer(500, 300);
        Assert.Null(r.NextDeadline);

        r.OnKey(1, true, HoldOnly, 1000);
        Assert.Equal(1500, r.NextDeadline);

        r.OnKey(1, false, HoldOnly, 1100);
        Assert.Null(r.NextDeadline);
    }

    [Fact]
    public void PerKeyHoldTime_Overrides_TheDefault()
    {
        var bound = new GestureBindings(true, false, HoldMs: 1500); // e.g. a Hold binding that shuts the PC down
        Assert.Equal("", Run(bound, (0, "down"), (600, "tick")));
        Assert.Equal("Hold", Run(bound, (0, "down"), (1500, "tick")));
    }

    // ── press + double ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void PressPlusDouble_SingleTap_FiresPressWhenTheWindowCloses()
    {
        Assert.Equal("", Run(DoubleOnly, (0, "down"), (50, "up"), (200, "tick")));       // window still open
        Assert.Equal("Press", Run(DoubleOnly, (0, "down"), (50, "up"), (351, "tick")));  // 300 ms after the release
    }

    [Fact]
    public void PressPlusDouble_TwoQuickTaps_FireOnlyDouble()
    {
        Assert.Equal("Double", Run(DoubleOnly, (0, "down"), (50, "up"), (200, "down"), (250, "up"), (900, "tick")));
    }

    [Fact]
    public void SecondTapAfterTheWindow_IsANewSingleTap()
    {
        // first tap resolves to Press at the late second press, which then starts its own window
        Assert.Equal("Press,Press", Run(DoubleOnly, (0, "down"), (50, "up"), (500, "down"), (550, "up"), (900, "tick")));
    }

    [Fact]
    public void DeadlineFor_TheDoubleWindow_IsAfterTheRelease()
    {
        var r = new GestureRecognizer(500, 300);
        r.OnKey(1, true, DoubleOnly, 0);
        r.OnKey(1, false, DoubleOnly, 50);
        Assert.Equal(350, r.NextDeadline);
    }

    // ── hold + double, mixed ───────────────────────────────────────────────────────────────

    [Fact]
    public void HoldPlusDouble_EachGestureFiresAlone()
    {
        Assert.Equal("Hold", Run(Both, (0, "down"), (500, "tick"), (900, "up"), (2000, "tick")));
        Assert.Equal("Double", Run(Both, (0, "down"), (50, "up"), (200, "down"), (250, "up"), (900, "tick")));
        Assert.Equal("Press", Run(Both, (0, "down"), (50, "up"), (900, "tick")));
    }

    [Fact]
    public void HoldOnTheSecondTap_DoesNotFireHold()
    {
        Assert.Equal("Double", Run(Both, (0, "down"), (50, "up"), (200, "down"), (1000, "tick"), (1200, "up"), (2000, "tick")));
    }

    [Fact]
    public void KeysAreIndependent()
    {
        var r = new GestureRecognizer(500, 300);
        r.OnKey(1, true, HoldOnly, 0);
        var other = r.OnKey(2, true, None, 100); // key 2 has no gestures: fires at once
        Assert.Equal(new[] { KeyGesture.Press }, other);

        var ticks = r.Tick(500);
        Assert.Equal(new[] { (1, KeyGesture.Hold) }, ticks);
    }

    [Fact]
    public void DuplicateDownEvents_AreIgnored()
    {
        Assert.Equal("Hold", Run(HoldOnly, (0, "down"), (100, "down"), (200, "down"), (500, "tick"), (900, "up")));
    }

    [Fact]
    public void Reset_ForgetsPendingGestures()
    {
        var r = new GestureRecognizer(500, 300);
        r.OnKey(1, true, HoldOnly, 0);
        r.Reset();
        Assert.Null(r.NextDeadline);
        Assert.Empty(r.Tick(10_000));
    }

    // ── the engine around it ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Engine_FiresHold_AfterTheConfiguredTime_AndOnlyOnce()
    {
        using var engine = new KeyInteractionEngine { HoldDurationMs = 80 };
        var fired = new List<(int, KeyGesture)>();
        engine.GestureTriggered += (k, g) => { lock (fired) fired.Add((k, g)); };

        engine.HandleGestureEvent(3, true, HoldOnly);
        await Task.Delay(400);
        engine.HandleGestureEvent(3, false, HoldOnly);
        await Task.Delay(100);

        lock (fired) Assert.Equal(new[] { (3, KeyGesture.Hold) }, fired);
    }

    [Fact]
    public async Task Engine_DoubleTap_FiresDouble_SingleTapFiresPressAfterTheWindow()
    {
        using var engine = new KeyInteractionEngine { DoublePressIntervalMs = 120 };
        var fired = new List<KeyGesture>();
        engine.GestureTriggered += (_, g) => { lock (fired) fired.Add(g); };

        engine.HandleGestureEvent(0, true, DoubleOnly);
        engine.HandleGestureEvent(0, false, DoubleOnly);
        engine.HandleGestureEvent(0, true, DoubleOnly);
        engine.HandleGestureEvent(0, false, DoubleOnly);
        await Task.Delay(350);
        lock (fired) Assert.Equal(new[] { KeyGesture.Double }, fired);

        fired.Clear();
        engine.HandleGestureEvent(0, true, DoubleOnly);
        engine.HandleGestureEvent(0, false, DoubleOnly);
        await Task.Delay(400);
        lock (fired) Assert.Equal(new[] { KeyGesture.Press }, fired);
    }

    [Fact]
    public async Task Engine_ThrowingHandler_DoesNotBreakLaterGestures()
    {
        using var engine = new KeyInteractionEngine { HoldDurationMs = 40 };
        int calls = 0;
        engine.GestureTriggered += (_, _) => { calls++; throw new InvalidOperationException("boom"); };

        engine.HandleGestureEvent(0, true, HoldOnly);
        await Task.Delay(200);
        engine.HandleGestureEvent(0, false, HoldOnly);
        engine.HandleGestureEvent(0, true, HoldOnly);
        await Task.Delay(200);

        Assert.Equal(2, calls);
    }

    // ── model & JSON compatibility ─────────────────────────────────────────────────────────

    [Fact]
    public void PreChangeProfileJson_LoadsWithoutGestures_AndSavesWithoutThem()
    {
        // frozen fixture: a key as written before T-05 (no "Gestures" property at all)
        // enums are stored as numbers in profile files
        string oldProfile = "{\"Id\":\"p\",\"Name\":\"Old\",\"Keys\":[{\"Index\":0,\"Action\":" + (int)DeckActionType.MediaPlayPause +
                            ",\"PressMode\":" + (int)KeyPressMode.OnPress + ",\"CustomTitle\":\"Play\"}]}";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var model = JsonSerializer.Deserialize<ProfileModel>(oldProfile, options)!;
        var key = model.Keys.Single();

        Assert.Null(key.Gestures);
        Assert.Equal(DeckActionType.MediaPlayPause, key.Action);
        Assert.Equal(KeyPressMode.OnPress, key.PressMode);
    }

    [Fact]
    public void Gestures_RoundTrip_AndIsEmptyIsNotSerialised()
    {
        var key = new KeyAssignmentModel
        {
            Index = 2,
            Action = DeckActionType.MediaPlayPause,
            Gestures = new KeyGestures
            {
                Hold = new KeyGestureBinding { Action = DeckActionType.Hotkey, ActionParameter = "Ctrl+M" },
                Double = new KeyGestureBinding { Action = DeckActionType.MediaNext }
            }
        };

        string json = JsonSerializer.Serialize(key);
        Assert.DoesNotContain("IsEmpty", json);

        var back = JsonSerializer.Deserialize<KeyAssignmentModel>(json)!;
        Assert.Equal("Ctrl+M", back.Gestures!.Hold!.ActionParameter);
        Assert.Equal(DeckActionType.MediaNext, back.Gestures.Double!.Action);
        Assert.False(back.Gestures.IsEmpty);
        Assert.True(new KeyGestures().IsEmpty);
        Assert.True(new KeyGestures { Hold = new KeyGestureBinding() }.IsEmpty);
    }

    // ── API: keys.N:hold / keys.N:double ───────────────────────────────────────────────────

    [Theory]
    [InlineData("keys.3", 3, KeyGesture.Press)]
    [InlineData("keys.3:hold", 3, KeyGesture.Hold)]
    [InlineData("keys.14:double", 14, KeyGesture.Double)]
    public void ControlIds_WithGestureSuffix_AreParsed(string id, int index, KeyGesture gesture)
    {
        Assert.Equal((index, gesture), ProfileValidator.ParseControl(id));
    }

    [Theory]
    [InlineData("keys.3:long")]
    [InlineData("keys.3:")]
    [InlineData("keys.15:hold")]
    public void BadGestureSuffix_IsRejected(string id) => Assert.Null(ProfileValidator.ParseControl(id));

    [Fact]
    public void Api_WritesAndClearsGestures_WithoutTouchingTheTap()
    {
        var profile = ProfileModel.CreateEmpty("x");
        var tap = new ControlChange { Control = "keys.1", Action = "MediaPlayPause", Title = "Play" };
        var hold = new ControlChange { Control = "keys.1:hold", Action = "Hotkey", Parameter = "Ctrl+M" };
        var dbl = new ControlChange { Control = "keys.1:double", Action = "MediaNext" };

        Assert.True(ProfileValidator.Validate(new[] { tap, hold, dbl }).IsValid);
        ProfileValidator.Apply(profile, new[] { tap, hold, dbl });

        var key = profile.Keys.Single(k => k.Index == 1);
        Assert.Equal(DeckActionType.MediaPlayPause, key.Action);
        Assert.Equal("Play", key.CustomTitle);
        Assert.Equal("Ctrl+M", key.Gestures!.Hold!.ActionParameter);
        Assert.Equal(DeckActionType.MediaNext, key.Gestures.Double!.Action);

        ProfileValidator.Apply(profile, new[]
        {
            new ControlChange { Control = "keys.1:hold", Clear = true },
            new ControlChange { Control = "keys.1:double", Clear = true }
        });
        Assert.Null(profile.Keys.Single(k => k.Index == 1).Gestures); // back to an old-style key
        Assert.Equal(DeckActionType.MediaPlayPause, profile.Keys.Single(k => k.Index == 1).Action);
    }

    [Fact]
    public void Api_RejectsTitleOnAGesture_AndAnInvalidGestureAction()
    {
        var result = ProfileValidator.Validate(new[]
        {
            new ControlChange { Control = "keys.1:hold", Action = "MediaNext", Title = "no" },
            new ControlChange { Control = "keys.2:double", Action = "NoSuchAction" }
        });

        Assert.Equal(2, result.Errors.Count);
    }
}

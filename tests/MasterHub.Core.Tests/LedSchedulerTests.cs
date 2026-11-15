using MasterHub.Core.Devices;

namespace MasterHub.Core.Tests;

/// <summary>T-18: the LED gate – coalescing, diffs, the 40 ms module gap, stale "music" drops and ordering. No thread, no sleeping.</summary>
public class LedSchedulerTests
{
    private static LedCommand Color(LedDevice d, byte index, byte r, byte g = 0, byte b = 0, bool droppable = false) =>
        new(d, index, LedKind.Color, r, g, b, droppable);

    private static (LedCommandScheduler Scheduler, List<LedCommand> Sent) Create(int result = 0)
    {
        var sent = new List<LedCommand>();
        var scheduler = new LedCommandScheduler(c => { sent.Add(c); return result; }, () => 0, startThread: false);
        return (scheduler, sent);
    }

    [Fact]
    public void Coalescing_LatestWins_PerTarget()
    {
        var (s, sent) = Create();

        for (byte v = 1; v <= 50; v++) s.Submit(Color(LedDevice.Faders, 2, v));   // a burst, e.g. a fader LED following a value
        s.Pump(0);

        var only = Assert.Single(sent);
        Assert.Equal(50, only.V1);
        Assert.Equal(49, s.CoalescedCount);
    }

    [Fact]
    public void Diff_AnUnchangedCommand_IsNotSentAgain()
    {
        var (s, sent) = Create();

        s.Submit(Color(LedDevice.Base, 0, 10, 20, 30));
        s.Pump(0);
        s.Submit(Color(LedDevice.Base, 0, 10, 20, 30));   // the same colour again
        s.Pump(100);
        s.Submit(Color(LedDevice.Base, 0, 11, 20, 30));
        s.Pump(200);

        Assert.Equal(2, sent.Count);
        Assert.Equal(1, s.SkippedUnchangedCount);
    }

    [Fact]
    public void KnobsAndFaders_GetAtLeast40ms_BetweenSends_TheBaseDoesNot()
    {
        var (s, sent) = Create();
        for (byte i = 0; i < 5; i++) s.Submit(Color(LedDevice.Faders, i, (byte)(i + 1)));
        for (byte i = 0; i < 4; i++) s.Submit(Color(LedDevice.Base, 0, (byte)(100 + i)));  // coalesces to one, but it is the base

        var times = new List<(long Time, LedDevice Device)>();
        long now = 0;
        for (int guard = 0; guard < 100 && s.PendingCount > 0; guard++)
        {
            int before = sent.Count;
            long? next = s.Pump(now);
            foreach (var c in sent.Skip(before)) times.Add((now, c.Device));
            now = next ?? now + 1;
        }

        var faderTimes = times.Where(t => t.Device == LedDevice.Faders).Select(t => t.Time).ToList();
        Assert.Equal(5, faderTimes.Count);
        for (int i = 1; i < faderTimes.Count; i++)
            Assert.True(faderTimes[i] - faderTimes[i - 1] >= LedCommandScheduler.ModuleGapMs, $"gap {faderTimes[i] - faderTimes[i - 1]} ms");

        Assert.Equal(0, times.Single(t => t.Device == LedDevice.Base).Time); // the base went at once, next to the first fader
    }

    [Fact]
    public void FiftyUpdatesPerSecond_NeverReachTheModule()
    {
        var (s, sent) = Create();
        long now = 0;
        byte value = 0;

        // 1 second, a fader LED updated every 20 ms (50/s – the rate that made faders drop off the bus)
        for (; now < 1000; now += 20)
        {
            s.Submit(Color(LedDevice.Faders, 0, ++value));
            s.Pump(now);
        }

        Assert.InRange(sent.Count, 1, 1000 / LedCommandScheduler.ModuleGapMs + 1); // at most ~25 sends
        Assert.True(sent.Count < 50);
    }

    [Fact]
    public void Ordering_FollowsSubmission_ModeBeforeItsColour()
    {
        var (s, sent) = Create();

        s.Submit(new LedCommand(LedDevice.Faders, 1, LedKind.Mode, (byte)LedEffect.Static));
        s.Submit(Color(LedDevice.Faders, 1, 255));

        long now = 0;
        while (s.PendingCount > 0) now = s.Pump(now) ?? now + 1;

        Assert.Equal(new[] { LedKind.Mode, LedKind.Color }, sent.Select(c => c.Kind));
    }

    [Fact]
    public void StaleMusicCommands_AreDropped_NotShownLate()
    {
        var (s, sent) = Create();
        s.Submit(Color(LedDevice.Faders, 0, 1));                    // a normal command occupies the module
        s.Submit(Color(LedDevice.Faders, 1, 2, droppable: true));    // a beat that has to wait for the gap
        s.Pump(0);

        // the gap ends 40 ms later, but by now the beat is older than the stale limit
        s.Pump(LedCommandScheduler.StaleMs + 50);

        Assert.Single(sent);
        Assert.Equal(1, s.DroppedStaleCount);
    }

    [Fact]
    public void FreshMusicCommands_AreSent()
    {
        var (s, sent) = Create();
        s.Submit(Color(LedDevice.Knobs, 0, 9, droppable: true));
        s.Pump(10);
        Assert.Single(sent);
    }

    [Fact]
    public void AllLeds_ThenOneLed_ThenAllAgain_IsResentCorrectly()
    {
        var (s, sent) = Create();
        long now = 0;
        void Settle() { while (s.PendingCount > 0) now = s.Pump(now) ?? now + 1; now += 100; }

        s.Submit(Color(LedDevice.Faders, 0xFF, 50)); Settle();      // all fader LEDs = 50
        s.Submit(Color(LedDevice.Faders, 2, 99)); Settle();         // LED 2 = 99 (all is no longer true)
        s.Submit(Color(LedDevice.Faders, 0xFF, 50)); Settle();      // all = 50 again: must NOT be skipped as "unchanged"

        Assert.Equal(new byte[] { 50, 99, 50 }, sent.Select(c => c.V1));
    }

    [Fact]
    public void FailedSend_IsNotRemembered_SoItIsSentAgain()
    {
        var (s, sent) = Create(result: 7); // device gone

        s.Submit(Color(LedDevice.Base, 0, 5));
        s.Pump(0);
        s.Submit(Color(LedDevice.Base, 0, 5));
        s.Pump(10);

        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void Clear_DropsWaitingCommands_AndInvalidateForcesAResend()
    {
        var (s, sent) = Create();
        s.Submit(Color(LedDevice.Faders, 0, 1));
        s.Submit(Color(LedDevice.Faders, 1, 2));
        s.Clear();                                // standby: nothing may light a sleeping deck later
        s.Pump(0);
        Assert.Empty(sent);

        s.Submit(Color(LedDevice.Base, 0, 7));
        s.Pump(0);
        s.Invalidate();                            // after a wake-up the module has forgotten its state
        s.Submit(Color(LedDevice.Base, 0, 7));
        s.Pump(10);

        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void Pump_ReportsWhenTheNextSendMayGo()
    {
        var (s, _) = Create();
        s.Submit(Color(LedDevice.Faders, 0, 1));
        s.Submit(Color(LedDevice.Faders, 1, 2));

        long? next = s.Pump(100);

        Assert.Equal(100 + LedCommandScheduler.ModuleGapMs, next);
        Assert.Null(new LedCommandScheduler(_ => 0, () => 0, startThread: false).Pump(0));
    }

    [Fact]
    public async Task RealThread_SendsAndStopsCleanly()
    {
        var sent = new List<LedCommand>();
        using var s = new LedCommandScheduler(c => { lock (sent) sent.Add(c); return 0; });

        s.Submit(Color(LedDevice.Base, 0, 1));
        s.Submit(Color(LedDevice.Faders, 0, 2));
        for (int i = 0; i < 50; i++)
        {
            lock (sent) if (sent.Count == 2) break;
            await Task.Delay(20);
        }

        lock (sent) Assert.Equal(2, sent.Count);
    }
}

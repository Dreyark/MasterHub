using System.Text.Json;
using MasterHub.Core.Profiles.Auto;

namespace MasterHub.Core.Tests;

/// <summary>T-37: counting the time a program is in front and the presses of a control. Local, opt-in, never forgets what it must not count.</summary>
public sealed class AssistUsageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_usage_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string UsageFile => Path.Combine(_dir, "usage.json");
    private string PressFile => Path.Combine(_dir, "presses.json");

    /// <summary>A clock and a "program in front" that the test moves by hand, so nothing sleeps.</summary>
    private sealed class Scene
    {
        public DateTime Now = new(2026, 11, 14, 9, 0, 0);
        public string? Front;
        public void Pass(int seconds) => Now = Now.AddSeconds(seconds);
    }

    private AppUsageService Usage(Scene scene) => new(UsageFile, () => scene.Front, () => scene.Now, useTimer: false);

    /// <summary>Looks at the front program every 5 seconds for <paramref name="seconds"/> seconds.</summary>
    private static void Watch(AppUsageService usage, Scene scene, int seconds)
    {
        for (int i = 0; i < seconds; i += 5)
        {
            scene.Pass(5);
            usage.Sample();
        }
    }

    // ── time in front ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheProgramInFront_IsCreditedWithTheTimeBetweenLooks()
    {
        var scene = new Scene { Front = "code" };
        var usage = Usage(scene);
        usage.Start();

        usage.Sample();                 // the first look only notes who is in front
        Watch(usage, scene, 60);
        scene.Front = "chrome";
        Watch(usage, scene, 30);        // the 5 s in which "code" was still in front are credited to it

        var snapshot = usage.Snapshot();
        Assert.Equal(65, snapshot["code"].Seconds);
        Assert.Equal(25, snapshot["chrome"].Seconds);
    }

    [Fact]
    public void OneStretch_CountsAtMostFifteenMinutes_AndStartsAgainWhenAnotherProgramComesToTheFront()
    {
        var scene = new Scene { Front = "game" };
        var usage = Usage(scene);
        usage.Start();
        usage.Sample();

        Watch(usage, scene, 40 * 60);   // left in front over lunch
        Assert.Equal(15 * 60, usage.Snapshot()["game"].Seconds);

        scene.Front = "notepad";
        Watch(usage, scene, 10);
        scene.Front = "game";
        Watch(usage, scene, 60);        // a new stretch: counted again
        Assert.True(usage.Snapshot()["game"].Seconds > 15 * 60);
    }

    [Fact]
    public void ALongGapBetweenLooks_ASleepingMachine_IsNotCounted()
    {
        var scene = new Scene { Front = "code" };
        var usage = Usage(scene);
        usage.Start();
        usage.Sample();

        scene.Pass(3 * 60 * 60);        // the laptop was shut
        usage.Sample();

        Assert.False(usage.Snapshot().ContainsKey("code"));
    }

    [Fact]
    public void NoProgramInFront_OrMasterHubItself_CountsForNobody()
    {
        var scene = new Scene { Front = null };   // the probe says null for the lock screen and for MasterHub's own window
        var usage = Usage(scene);
        usage.Start();
        usage.Sample();
        Watch(usage, scene, 120);

        Assert.Empty(usage.Snapshot());
    }

    [Fact]
    public void Days_GrowOncePerDay()
    {
        var scene = new Scene { Front = "code" };
        var usage = Usage(scene);
        usage.Start();
        usage.Sample();

        Watch(usage, scene, 60);
        Assert.Equal(1, usage.Snapshot()["code"].Days);

        scene.Now = new DateTime(2026, 11, 15, 9, 0, 0);
        usage.Sample();                 // the gap is not counted, but it is a new day
        Watch(usage, scene, 30);
        Watch(usage, scene, 30);
        Assert.Equal(2, usage.Snapshot()["code"].Days);
    }

    [Fact]
    public void AnotherProgramComingToTheFront_IsAnnounced_OnceEach()
    {
        var scene = new Scene { Front = "code" };
        var usage = Usage(scene);
        var seen = new List<string>();
        usage.AppInFront += seen.Add;
        usage.Start();

        usage.Sample();
        Watch(usage, scene, 20);        // same program: no new announcement
        scene.Front = "chrome";
        Watch(usage, scene, 10);
        scene.Front = null;             // nothing in front is not "a program came to the front"
        Watch(usage, scene, 10);

        Assert.Equal(new[] { "code", "chrome" }, seen);
    }

    [Fact]
    public void NothingIsLookedAt_UntilItIsStarted_AndNotAfterItStopped()
    {
        var scene = new Scene { Front = "code" };
        var usage = Usage(scene);

        Watch(usage, scene, 60);
        Assert.False(usage.IsRunning);
        Assert.Empty(usage.Snapshot());

        usage.Start();
        usage.Sample();
        Watch(usage, scene, 30);
        usage.Stop();
        long counted = usage.Snapshot()["code"].Seconds;

        Watch(usage, scene, 60);
        Assert.Equal(counted, usage.Snapshot()["code"].Seconds);
    }

    // ── the file ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFile_IsWrittenAtMostEveryTwoMinutes_AndWhenCountingStops_AndRecordsSince()
    {
        var scene = new Scene { Front = "code" };
        var usage = Usage(scene);
        usage.Start();
        usage.Sample();
        Watch(usage, scene, 10);        // the first change (5 s) is written straight away, the second is not

        Assert.True(File.Exists(UsageFile));
        long onDisk() => JsonSerializer.Deserialize<AppUsageData>(File.ReadAllText(UsageFile))!.Apps["code"].Seconds;
        Assert.Equal(5, onDisk());

        Watch(usage, scene, 60);        // within two minutes: not written again
        Assert.Equal(5, onDisk());

        Watch(usage, scene, 120);       // two minutes have passed
        Assert.True(onDisk() > 5);

        Watch(usage, scene, 20);
        usage.Stop();                   // and on exit
        Assert.Equal(usage.Snapshot()["code"].Seconds, onDisk());
        Assert.Equal(new DateTime(2026, 11, 14, 9, 0, 0), JsonSerializer.Deserialize<AppUsageData>(File.ReadAllText(UsageFile))!.Since);
    }

    [Fact]
    public void TheNumbers_SurviveARestart()
    {
        var scene = new Scene { Front = "code" };
        var first = Usage(scene);
        first.Start();
        first.Sample();
        Watch(first, scene, 60);
        first.Stop();

        var again = Usage(scene);
        Assert.Equal(60, again.Snapshot()["code"].Seconds);
        Assert.Equal(new DateTime(2026, 11, 14, 9, 0, 0), again.Since);
    }

    [Fact]
    public void ACorruptFile_JustStartsAgain()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(UsageFile, "{ not json");

        var usage = Usage(new Scene());

        Assert.Empty(usage.Snapshot());
    }

    [Fact]
    public void Clear_ForgetsEverything_AndSinceRestarts()
    {
        var scene = new Scene { Front = "code" };
        var usage = Usage(scene);
        usage.Start();
        usage.Sample();
        Watch(usage, scene, 60);

        scene.Pass(3600);
        usage.Clear();

        Assert.Empty(usage.Snapshot());
        Assert.Equal(scene.Now, usage.Since);
        Assert.Empty(new AppUsageService(UsageFile, () => null, () => scene.Now, useTimer: false).Snapshot());
    }

    [Fact]
    public void ProgramNames_AreCompared_WithoutCase()
    {
        var scene = new Scene { Front = "Code" };
        var usage = Usage(scene);
        usage.Start();
        usage.Sample();
        Watch(usage, scene, 10);
        scene.Front = "CODE";
        Watch(usage, scene, 10);

        Assert.Single(usage.Snapshot());
        Assert.Equal(20, usage.Snapshot()["code"].Seconds);
    }

    // ── presses ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Presses_AreNotCounted_WhileCountingIsOff_AndNothingIsWritten()
    {
        bool on = false;
        var counter = new PressCounter(() => on, PressFile);

        counter.Count("profile", "p:0:3");
        counter.Flush();

        Assert.Empty(counter.Get("profile"));
        Assert.False(File.Exists(PressFile));
        Assert.Null(counter.Since);
    }

    [Fact]
    public void Presses_AreCountedPerProfileAndControl()
    {
        var counter = new PressCounter(() => true, PressFile);

        counter.Count("a", PressCounter.KeyId("p:0", 3));
        counter.Count("a", PressCounter.KeyId("p:0", 3));
        counter.Count("a", PressCounter.KeyId("p:0", 4));
        counter.Count("b", PressCounter.KeyId("p:0", 3));

        Assert.Equal(2, counter.Get("a")["p:0:3"]);
        Assert.Equal(1, counter.Get("a")["p:0:4"]);
        Assert.Equal(1, counter.Get("b")["p:0:3"]);
        Assert.Empty(counter.Get("nobody"));
    }

    [Fact]
    public void Presses_AreWrittenRarely_ButFlushWritesNow_AndTheyComeBack()
    {
        var now = new DateTime(2026, 11, 14, 9, 0, 0);
        var counter = new PressCounter(() => true, PressFile, () => now);

        counter.Count("a", "p:0:1");            // the first change is written at once
        counter.Count("a", "p:0:1");            // within two minutes: only in memory
        Assert.Equal(1, JsonSerializer.Deserialize<PressData>(File.ReadAllText(PressFile))!.Profiles["a"]["p:0:1"]);

        counter.Flush();
        Assert.Equal(2, JsonSerializer.Deserialize<PressData>(File.ReadAllText(PressFile))!.Profiles["a"]["p:0:1"]);

        var again = new PressCounter(() => true, PressFile, () => now);
        Assert.Equal(2, again.Get("a")["p:0:1"]);
        Assert.Equal(now, again.Since);
    }

    [Fact]
    public void Clear_DeletesTheFile_AndTheNumbers()
    {
        var counter = new PressCounter(() => true, PressFile);
        counter.Count("a", "p:0:1");
        counter.Flush();
        Assert.True(File.Exists(PressFile));

        counter.Clear();

        Assert.False(File.Exists(PressFile));
        Assert.Empty(counter.Get("a"));
    }
}

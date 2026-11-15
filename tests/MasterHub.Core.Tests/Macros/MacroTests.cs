using MasterHub.Core.Macros;
using MasterHub.Core.Tests.Integrations;

namespace MasterHub.Core.Tests.Macros;

internal sealed class FakeSimulator : IInputSimulator
{
    public readonly List<string> Log = new();
    private void Add(string s) { lock (Log) Log.Add(s); }
    public string[] Snapshot() { lock (Log) return Log.ToArray(); }
    public void KeyDown(int vk, bool extended) => Add($"KD {vk:X}");
    public void KeyUp(int vk, bool extended) => Add($"KU {vk:X}");
    public void MouseDown(MacroMouseButton button) => Add($"MD {button}");
    public void MouseUp(MacroMouseButton button) => Add($"MU {button}");
    public void Wheel(int delta, bool horizontal) => Add($"WH {(horizontal ? "H" : "V")}{delta}");
    public void TypeText(string text) => Add($"TX {text}");
}

internal sealed class FakeSource : IInputEventSource
{
    public Func<InputEvent, bool>? Handler;
    public bool IsRunning { get; private set; }
    public int StartCalls, StopCalls;
    public void Start(Func<InputEvent, bool> intercept) { Handler = intercept; IsRunning = true; StartCalls++; }
    public void Stop() { IsRunning = false; StopCalls++; }
    public void Dispose() { }

    public bool Push(InputKind kind, int code, bool down, bool razer = true, bool injected = false, bool own = false, int delta = 0) =>
        Handler!(new InputEvent((InputEventKind)kind, code, down, delta, false, injected, own, razer));
}

internal enum InputKind { Key = 0, Mouse = 1, Wheel = 2, HWheel = 3 }

public class MacroEngineTests
{
    private static MacroDefinition Macro(MacroPlaybackMode mode, params MacroStep[] steps) =>
        new() { Name = "t", Mode = mode, Steps = steps.ToList() };

    private static Task FastYield(int ms, CancellationToken ct) => Task.Delay(1, ct);

    [Fact]
    public async Task Once_ExecutesStepsInOrder_AndHonoursDelays()
    {
        var sim = new FakeSimulator();
        var delays = new List<int>();
        using var engine = new MacroEngine(sim, (ms, ct) => { delays.Add(ms); return Task.CompletedTask; });
        var m = Macro(MacroPlaybackMode.Once,
            MacroStep.KeyDown(0x41), MacroStep.Delay(30), MacroStep.KeyUp(0x41),
            MacroStep.MouseDown(MacroMouseButton.Left), MacroStep.MouseUp(MacroMouseButton.Left),
            MacroStep.Wheel(-120), MacroStep.TypeText("hej"));

        await engine.RunLoopAsync(m, CancellationToken.None);

        Assert.Equal(new[] { "KD 41", "KU 41", "MD Left", "MU Left", "WH V-120", "TX hej" }, sim.Snapshot());
        Assert.Equal(new[] { 30 }, delays);
    }

    [Fact]
    public async Task Cancelled_Macro_ReleasesEverythingItHeld()
    {
        var sim = new FakeSimulator();
        using var engine = new MacroEngine(sim);   // prawdziwe opóźnienia
        var m = Macro(MacroPlaybackMode.Once,
            MacroStep.KeyDown(0x11), MacroStep.KeyDown(0x43), MacroStep.MouseDown(MacroMouseButton.Middle), MacroStep.Delay(60_000));

        Assert.True(engine.Start(m));
        Assert.True(await ObsIntegrationTests.WaitFor(() => sim.Snapshot().Length == 3));
        var cancelledTask = engine.GetRunTask(m.Id)!;
        engine.Stop(m.Id);
        await cancelledTask;

        var log = sim.Snapshot();
        Assert.Contains("KU 43", log);     // zwolnione w odwrotnej kolejności
        Assert.Contains("KU 11", log);
        Assert.Contains("MU Middle", log);
        Assert.True(Array.IndexOf(log, "KU 43") < Array.IndexOf(log, "KU 11"));
        Assert.False(engine.IsRunning(m.Id));
    }

    [Fact]
    public async Task WhilePressed_RepeatsUntilTriggerReleased()
    {
        var sim = new FakeSimulator();
        using var engine = new MacroEngine(sim, FastYield);
        var m = Macro(MacroPlaybackMode.WhilePressed, MacroStep.KeyDown(0x41), MacroStep.KeyUp(0x41));

        engine.TriggerDown(m);
        Assert.True(await ObsIntegrationTests.WaitFor(() => sim.Snapshot().Count(s => s == "KD 41") >= 3));
        var loopTask = engine.GetRunTask(m.Id)!;
        engine.TriggerUp(m);
        await loopTask;

        var log = sim.Snapshot();
        Assert.Equal(log.Count(s => s == "KD 41"), log.Count(s => s == "KU 41"));   // zawsze sparowane
        Assert.False(engine.IsRunning(m.Id));
    }

    [Fact]
    public async Task Toggle_SecondPressStops()
    {
        var sim = new FakeSimulator();
        using var engine = new MacroEngine(sim, FastYield);
        var m = Macro(MacroPlaybackMode.Toggle, MacroStep.KeyDown(0x42), MacroStep.KeyUp(0x42));

        engine.TriggerDown(m);
        Assert.True(await ObsIntegrationTests.WaitFor(() => engine.IsRunning(m.Id) && sim.Snapshot().Length >= 4));
        engine.TriggerUp(m);                       // puszczenie nic nie zmienia w trybie Toggle
        Assert.True(engine.IsRunning(m.Id));
        var runTask = engine.GetRunTask(m.Id)!;
        engine.TriggerDown(m);                     // drugie naciśnięcie
        await runTask;

        Assert.False(engine.IsRunning(m.Id));
    }

    [Fact]
    public async Task RepeatCount_RunsExactlyNTimes_WithGap()
    {
        var sim = new FakeSimulator();
        var delays = new List<int>();
        using var engine = new MacroEngine(sim, (ms, ct) => { delays.Add(ms); return Task.CompletedTask; });
        var m = Macro(MacroPlaybackMode.RepeatCount, MacroStep.KeyDown(0x44), MacroStep.KeyUp(0x44));
        m.RepeatCount = 3;
        m.RepeatDelayMs = 40;

        await engine.RunLoopAsync(m, CancellationToken.None);

        Assert.Equal(3, sim.Snapshot().Count(s => s == "KD 44"));
        Assert.Equal(new[] { 40, 40 }, delays);    // przerwa tylko pomiędzy przebiegami
    }

    [Fact]
    public async Task LoopWithoutAnyDelay_GetsMinimumGap_SoItCannotSpinTheCpu()
    {
        var sim = new FakeSimulator();
        var delays = new List<int>();
        using var engine = new MacroEngine(sim, (ms, ct) => { delays.Add(ms); return Task.CompletedTask; });
        var m = Macro(MacroPlaybackMode.RepeatCount, MacroStep.KeyDown(0x45), MacroStep.KeyUp(0x45));
        m.RepeatCount = 3;

        await engine.RunLoopAsync(m, CancellationToken.None);

        Assert.All(delays, d => Assert.Equal(MacroEngine.MinLoopGapMs, d));
    }

    [Fact]
    public void EmptyMacro_IsNotStarted_AndSecondStartWhileRunningIsRejected()
    {
        var sim = new FakeSimulator();
        using var engine = new MacroEngine(sim);
        Assert.False(engine.Start(Macro(MacroPlaybackMode.Once)));

        var m = Macro(MacroPlaybackMode.Once, MacroStep.Delay(60_000));
        Assert.True(engine.Start(m));
        Assert.False(engine.Start(m));
        engine.StopAll();
    }

    [Fact]
    public async Task EditingDefinitionDuringPlayback_DoesNotAffectRunningSequence()
    {
        var sim = new FakeSimulator();
        var gate = new TaskCompletionSource();
        using var engine = new MacroEngine(sim, (ms, ct) => gate.Task);
        var m = Macro(MacroPlaybackMode.Once, MacroStep.KeyDown(0x46), MacroStep.Delay(10), MacroStep.KeyUp(0x46));

        engine.Start(m);
        Assert.True(await ObsIntegrationTests.WaitFor(() => sim.Snapshot().Length == 1));
        m.Steps.Clear();   // edycja w UI w trakcie odtwarzania
        var runTask = engine.GetRunTask(m.Id)!;
        gate.SetResult();
        await runTask;

        Assert.Equal(new[] { "KD 46", "KU 46" }, sim.Snapshot());
    }
}

public class MacroRecorderTests
{
    [Fact]
    public void RecordedMode_StoresActualGaps_AndSkipsAutoRepeatAndOrphanReleases()
    {
        long now = 1000;
        var rec = new MacroRecorder(MacroDelayMode.Recorded, nowMs: () => now);

        rec.OnMouseButton(MacroMouseButton.Left, false);   // zwolnienie kliknięcia "Nagrywaj" - osierocone
        rec.OnKey(0x41, true);
        now += 5; rec.OnKey(0x41, true);                   // autopowtarzanie
        now += 120; rec.OnKey(0x41, false);
        now += 80; rec.OnWheel(-120, false);

        var steps = rec.Build(trimTrailingStopClick: false);
        Assert.Equal(new[]
        {
            MacroStepType.KeyDown, MacroStepType.Delay, MacroStepType.KeyUp, MacroStepType.Delay, MacroStepType.Wheel
        }, steps.Select(s => s.Type).ToArray());
        Assert.Equal(125, steps[1].DelayMs);   // od pierwszego KeyDown (autorepeat nie przesuwa znacznika czasu)
        Assert.Equal(80, steps[3].DelayMs);
    }

    [Fact]
    public void FixedAndNoneModes_ProduceConstantOrNoDelays()
    {
        long now = 0;
        var fixedRec = new MacroRecorder(MacroDelayMode.Fixed, 25, () => now);
        var noneRec = new MacroRecorder(MacroDelayMode.None, 25, () => now);
        foreach (var r in new[] { fixedRec, noneRec })
        {
            now = 0; r.OnKey(0x41, true);
            now = 500; r.OnKey(0x41, false);
            now = 900; r.OnKey(0x42, true);
            now = 950; r.OnKey(0x42, false);
        }

        Assert.Equal(new[] { 25, 25, 25 }, fixedRec.Build(false).Where(s => s.Type == MacroStepType.Delay).Select(s => s.DelayMs).ToArray());
        Assert.DoesNotContain(noneRec.Build(false), s => s.Type == MacroStepType.Delay);
    }

    [Fact]
    public void Build_TrimsStopClick_AndReleasesStillHeldInputs()
    {
        long now = 0;
        var rec = new MacroRecorder(MacroDelayMode.Recorded, nowMs: () => now);
        rec.OnKey(0x10, true);                                   // Shift wciśnięty i nigdy nie puszczony
        now += 50; rec.OnMouseButton(MacroMouseButton.Left, true);   // kliknięcie "Zatrzymaj"
        now += 60; rec.OnMouseButton(MacroMouseButton.Left, false);

        var steps = rec.Build(trimTrailingStopClick: true);

        Assert.DoesNotContain(steps, s => s.Type is MacroStepType.MouseDown or MacroStepType.MouseUp);
        Assert.Equal(MacroStepType.KeyUp, steps[^1].Type);       // makro nie zostawia Shifta wciśniętego
        Assert.Equal(0x10, steps[^1].Key);
    }

    [Fact]
    public void Build_KeepsLegitimateClickWhenStopWasNotAClick()
    {
        var rec = new MacroRecorder(MacroDelayMode.None);
        rec.OnMouseButton(MacroMouseButton.Left, true);
        rec.OnMouseButton(MacroMouseButton.Left, false);

        Assert.Equal(2, rec.Build(trimTrailingStopClick: false).Count);
    }
}

public class MacroServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_macro_" + Guid.NewGuid().ToString("N"));
    private readonly FakeSimulator _sim = new();
    private readonly FakeSource _src = new();
    private readonly MacroService _svc;

    public MacroServiceTests()
    {
        _svc = new MacroService(new MacroStore(_dir), _sim, _src, (ms, ct) => Task.Delay(1, ct));
        _svc.Start();
    }

    public void Dispose() { _svc.Dispose(); try { Directory.Delete(_dir, true); } catch { } }

    private MacroDefinition AddMacro(MacroPlaybackMode mode = MacroPlaybackMode.Once)
    {
        var m = new MacroDefinition { Name = "m", Mode = mode, Steps = { MacroStep.KeyDown(0x58), MacroStep.KeyUp(0x58) } };
        _svc.SaveMacro(m);
        return m;
    }

    [Fact]
    public void NoBindings_MeansNoHooksAtAll()
    {
        Assert.False(_src.IsRunning);
        AddMacro();
        Assert.False(_src.IsRunning);   // samo makro bez przypisania nie wymaga haków
    }

    [Fact]
    public async Task MouseButtonBinding_RunsMacro_SwallowsDownAndUp_OnlyForRazer()
    {
        var m = AddMacro();
        _svc.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.MouseButton, Code = (int)MacroMouseButton.X1, MacroId = m.Id, OnlyRazerMouse = true });
        Assert.True(_src.IsRunning);

        Assert.False(_src.Push(InputKind.Mouse, (int)MacroMouseButton.X1, true, razer: false));   // inna mysz - przepuszczone
        Assert.False(_src.Push(InputKind.Mouse, (int)MacroMouseButton.X1, false, razer: false));
        Assert.Empty(_sim.Snapshot());

        Assert.True(_src.Push(InputKind.Mouse, (int)MacroMouseButton.X1, true, razer: true));     // Razer - połknięte
        Assert.True(_src.Push(InputKind.Mouse, (int)MacroMouseButton.X1, false, razer: true));
        Assert.True(await ObsIntegrationTests.WaitFor(() => _sim.Snapshot().Length == 2));
        Assert.Equal(new[] { "KD 58", "KU 58" }, _sim.Snapshot());
    }

    [Fact]
    public void AnyMouseBinding_AcceptsNonRazerDevice()
    {
        var m = AddMacro();
        _svc.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.MouseButton, Code = (int)MacroMouseButton.Middle, MacroId = m.Id, OnlyRazerMouse = false });

        Assert.True(_src.Push(InputKind.Mouse, (int)MacroMouseButton.Middle, true, razer: false));
    }

    [Fact]
    public async Task KeyBinding_IgnoresAutoRepeat_AndInjectedOrOwnEvents()
    {
        var m = AddMacro();
        _svc.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.Key, Code = 0x7C /* F13 */, MacroId = m.Id });

        Assert.False(_src.Push(InputKind.Key, 0x7C, true, injected: true));    // wstrzyknięte przez inną aplikację
        Assert.False(_src.Push(InputKind.Key, 0x7C, true, own: true));         // nasze własne
        Assert.Empty(_sim.Snapshot());

        Assert.True(_src.Push(InputKind.Key, 0x7C, true));
        Assert.True(_src.Push(InputKind.Key, 0x7C, true));                     // autopowtarzanie - połknięte, bez drugiego uruchomienia
        Assert.True(_src.Push(InputKind.Key, 0x7C, false));
        await ObsIntegrationTests.WaitFor(() => _sim.Snapshot().Length >= 2);
        await Task.Delay(100);
        Assert.Single(_sim.Snapshot(), s => s == "KD 58");
    }

    [Fact]
    public async Task WheelTilt_RunsOnce_AndIsSwallowed()
    {
        var m = AddMacro();
        _svc.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.WheelTiltRight, MacroId = m.Id, OnlyRazerMouse = true });

        Assert.False(_src.Push(InputKind.HWheel, 0, true, delta: -120));   // lewo - brak przypisania
        Assert.True(_src.Push(InputKind.HWheel, 0, true, delta: 120));
        Assert.True(await ObsIntegrationTests.WaitFor(() => _sim.Snapshot().Length == 2));
    }

    [Fact]
    public async Task PauseKey_StopsRunningLoops_Immediately()
    {
        var m = AddMacro(MacroPlaybackMode.Toggle);
        _svc.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.Key, Code = 0x7D, MacroId = m.Id });
        _src.Push(InputKind.Key, 0x7D, true);
        _src.Push(InputKind.Key, 0x7D, false);
        Assert.True(await ObsIntegrationTests.WaitFor(() => _svc.IsRunning(m.Id)));

        Assert.False(_src.Push(InputKind.Key, MacroService.PanicKey, true));   // klawisz awaryjny nie jest połykany

        Assert.True(await ObsIntegrationTests.WaitFor(() => !_svc.IsRunning(m.Id)));
    }

    [Fact]
    public void LeftAndRightButtons_CanNeverBeTriggers()
    {
        var m = AddMacro();
        Assert.Throws<ArgumentException>(() => _svc.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.MouseButton, Code = (int)MacroMouseButton.Left, MacroId = m.Id }));
        Assert.Throws<ArgumentException>(() => _svc.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.MouseButton, Code = (int)MacroMouseButton.Right, MacroId = m.Id }));
    }

    [Fact]
    public void DeletingLastBinding_StopsTheHooks_AndDeletingMacroRemovesItsBindings()
    {
        var m = AddMacro();
        var b = new MacroBinding { TriggerKind = MacroTriggerKind.Key, Code = 0x7C, MacroId = m.Id };
        _svc.SaveBinding(b);
        Assert.True(_src.IsRunning);

        _svc.DeleteMacro(m.Id);

        Assert.Empty(_svc.Bindings);
        Assert.False(_src.IsRunning);
    }

    [Fact]
    public void Recording_UsesHooksOnlyWhileActive_AndReturnsSteps()
    {
        long unused = 0; _ = unused;
        Assert.False(_src.IsRunning);
        _svc.StartRecording(MacroDelayMode.None);
        Assert.True(_src.IsRunning);
        Assert.True(_svc.IsRecording);

        Assert.False(_src.Push(InputKind.Key, 0x41, true));    // nagrywanie niczego nie połyka
        Assert.False(_src.Push(InputKind.Key, 0x41, false));
        Assert.False(_src.Push(InputKind.Mouse, (int)MacroMouseButton.Left, true));
        Assert.False(_src.Push(InputKind.Mouse, (int)MacroMouseButton.Left, false));   // "Zatrzymaj"
        Assert.False(_src.Push(InputKind.Key, 0x42, true, own: true));                 // własne zdarzenia nie są nagrywane

        var steps = _svc.StopRecording(trimTrailingStopClick: true);

        Assert.Equal(new[] { MacroStepType.KeyDown, MacroStepType.KeyUp }, steps.Select(s => s.Type).ToArray());
        Assert.False(_svc.IsRecording);
        Assert.False(_src.IsRunning);
    }

    [Fact]
    public async Task PlayFromDeck_RunsMacro_AndLoopsToggle()
    {
        var once = AddMacro();
        _svc.Play(once.Id);
        Assert.True(await ObsIntegrationTests.WaitFor(() => _sim.Snapshot().Length == 2));

        var loop = AddMacro(MacroPlaybackMode.Toggle);
        _svc.Play(loop.Id);
        Assert.True(await ObsIntegrationTests.WaitFor(() => _svc.IsRunning(loop.Id)));
        _svc.Play(loop.Id);   // drugi raz z klawisza decka = stop
        Assert.True(await ObsIntegrationTests.WaitFor(() => !_svc.IsRunning(loop.Id)));
    }

    [Fact]
    public void Library_PersistsAcrossRestarts()
    {
        var m = AddMacro(MacroPlaybackMode.RepeatCount);
        m.RepeatCount = 7;
        _svc.SaveMacro(m);
        _svc.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.Key, Code = 0x7E, MacroId = m.Id });

        using var again = new MacroService(new MacroStore(_dir), new FakeSimulator(), new FakeSource());
        again.Start();

        var loaded = Assert.Single(again.Macros);
        Assert.Equal(7, loaded.RepeatCount);
        Assert.Equal(MacroPlaybackMode.RepeatCount, loaded.Mode);
        Assert.Equal(2, loaded.Steps.Count);
        Assert.Single(again.Bindings);
    }
}

public class MacroStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_macrostore_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Load_DropsBindingsToMissingMacros_AndLeftRightTriggers_AndClampsValues()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "macros.json"), """
        {
          "Macros": [ { "Id": "a", "Name": "", "RepeatCount": 999999, "RepeatDelayMs": -5,
                        "Steps": [ { "Type": "Delay", "DelayMs": 99999999 } ] } ],
          "Bindings": [
            { "Id": "b1", "TriggerKind": "MouseButton", "Code": 3, "MacroId": "a" },
            { "Id": "b2", "TriggerKind": "MouseButton", "Code": 0, "MacroId": "a" },
            { "Id": "b3", "TriggerKind": "Key", "Code": 124, "MacroId": "missing" }
          ]
        }
        """);

        var lib = new MacroStore(_dir).Load();

        var macro = Assert.Single(lib.Macros);
        Assert.Equal(MacroEngine.MaxRepeatCount, macro.RepeatCount);
        Assert.Equal(0, macro.RepeatDelayMs);
        Assert.Equal(600_000, macro.Steps[0].DelayMs);
        Assert.False(string.IsNullOrWhiteSpace(macro.Name));
        Assert.Equal("b1", Assert.Single(lib.Bindings).Id);
    }

    [Fact]
    public void CorruptFile_YieldsEmptyLibrary_AndKeepsACopy()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "macros.json");
        File.WriteAllText(path, "{ this is not json");

        var lib = new MacroStore(_dir).Load();

        Assert.Empty(lib.Macros);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void VirtualKeyNames_DescribeKeysAndSteps()
    {
        Assert.Equal("A", VirtualKeyNames.Get(0x41));
        Assert.Equal("F13", VirtualKeyNames.Get(0x7C));
        Assert.Equal("F24", VirtualKeyNames.Get(0x87));
        Assert.True(VirtualKeyNames.IsExtended(0x25));   // strzałka w lewo
        Assert.False(VirtualKeyNames.IsExtended(0x41));
        Assert.Contains("120", VirtualKeyNames.Describe(MacroStep.Wheel(120)));
    }
}

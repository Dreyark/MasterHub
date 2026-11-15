using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Macros;

public sealed class MacroService : IMacroService
{
    /// <summary>Klawisz bezpieczeństwa: Pause/Break natychmiast zatrzymuje wszystkie działające makra.</summary>
    public const int PanicKey = 0x13;

    private readonly MacroStore _store;
    private readonly MacroEngine _engine;
    private readonly IInputEventSource _source;
    private readonly object _lock = new();

    private MacroLibrary _library = new();
    private volatile Snapshot _snapshot = Snapshot.Empty;
    private MacroRecorder? _recorder;
    private readonly HashSet<(MacroTriggerKind, int)> _heldTriggers = new();
    private bool _started, _disposed;

    private sealed class Snapshot
    {
        public static readonly Snapshot Empty = new(new Dictionary<string, MacroDefinition>(), new Dictionary<(MacroTriggerKind, int), List<MacroBinding>>());
        public Dictionary<string, MacroDefinition> Macros { get; }
        public Dictionary<(MacroTriggerKind, int), List<MacroBinding>> ByTrigger { get; }
        public Snapshot(Dictionary<string, MacroDefinition> m, Dictionary<(MacroTriggerKind, int), List<MacroBinding>> t) { Macros = m; ByTrigger = t; }
    }

    public event Action? LibraryChanged;
    public event Action<string, bool>? MacroRunningChanged;
    public event Action<bool>? RecordingStateChanged;
    public event Action<int>? RecordedStepCountChanged;

    public MacroService(MacroStore? store = null, IInputSimulator? simulator = null, IInputEventSource? source = null,
        Func<int, System.Threading.CancellationToken, System.Threading.Tasks.Task>? delay = null)
    {
        _store = store ?? new MacroStore();
        _engine = new MacroEngine(simulator ?? new WindowsInputSimulator(), delay);
        _source = source ?? new WindowsInputHooks();
        _engine.RunningChanged += (id, running) => MacroRunningChanged?.Invoke(id, running);
    }

    public IReadOnlyList<MacroDefinition> Macros { get { lock (_lock) return _library.Macros.ToList(); } }
    public IReadOnlyList<MacroBinding> Bindings { get { lock (_lock) return _library.Bindings.ToList(); } }
    public bool IsRecording => _recorder != null;

    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed) return;
            _started = true;
            _library = _store.Load();
            RebuildSnapshot();
        }
        RefreshHooks();
    }

    public MacroDefinition? GetMacro(string id)
    {
        lock (_lock) return _library.Macros.FirstOrDefault(m => m.Id == id)?.Clone();
    }

    public bool IsRunning(string macroId) => _engine.IsRunning(macroId);

    public void SaveMacro(MacroDefinition macro)
    {
        if (macro == null) return;
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(macro.Id)) macro.Id = Guid.NewGuid().ToString("N");
            macro.RepeatCount = Math.Clamp(macro.RepeatCount, 1, MacroEngine.MaxRepeatCount);
            var copy = macro.Clone();
            int idx = _library.Macros.FindIndex(m => m.Id == copy.Id);
            if (idx >= 0) _library.Macros[idx] = copy; else _library.Macros.Add(copy);
            PersistAndRebuild();
        }
        Changed();
    }

    public void DeleteMacro(string id)
    {
        _engine.Stop(id);
        lock (_lock)
        {
            _library.Macros.RemoveAll(m => m.Id == id);
            _library.Bindings.RemoveAll(b => b.MacroId == id);
            PersistAndRebuild();
        }
        Changed();
    }

    public void SaveBinding(MacroBinding binding)
    {
        if (binding == null) return;
        // Lewy/prawy przycisk myszy nie mogą być wyzwalaczami - zablokowałoby to normalną obsługę komputera.
        if (binding.TriggerKind == MacroTriggerKind.MouseButton &&
            binding.Code is (int)MacroMouseButton.Left or (int)MacroMouseButton.Right)
        {
            throw new ArgumentException("Lewy i prawy przycisk myszy nie mogą być wyzwalaczem makra.", nameof(binding));
        }

        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(binding.Id)) binding.Id = Guid.NewGuid().ToString("N");
            int idx = _library.Bindings.FindIndex(b => b.Id == binding.Id);
            var copy = new MacroBinding
            {
                Id = binding.Id, TriggerKind = binding.TriggerKind, Code = binding.Code,
                MacroId = binding.MacroId, Enabled = binding.Enabled, OnlyRazerMouse = binding.OnlyRazerMouse
            };
            if (idx >= 0) _library.Bindings[idx] = copy; else _library.Bindings.Add(copy);
            PersistAndRebuild();
        }
        Changed();
    }

    public void DeleteBinding(string id)
    {
        lock (_lock)
        {
            _library.Bindings.RemoveAll(b => b.Id == id);
            PersistAndRebuild();
        }
        Changed();
    }

    public void Play(string macroId)
    {
        MacroDefinition? macro;
        lock (_lock) macro = _library.Macros.FirstOrDefault(m => m.Id == macroId);
        if (macro != null) _engine.PlayFromDeck(macro);
    }

    public void Stop(string macroId) => _engine.Stop(macroId);
    public void StopAll() => _engine.StopAll();

    // ---------------------------------------------------------------- nagrywanie

    public void StartRecording(MacroDelayMode mode, int fixedDelayMs = 50)
    {
        lock (_lock)
        {
            if (_recorder != null) return;
            _engine.StopAll();
            _recorder = new MacroRecorder(mode, fixedDelayMs);
        }
        RefreshHooks();
        RecordingStateChanged?.Invoke(true);
    }

    public List<MacroStep> StopRecording(bool trimTrailingStopClick = true)
    {
        List<MacroStep> steps;
        lock (_lock)
        {
            if (_recorder == null) return new List<MacroStep>();
            steps = _recorder.Build(trimTrailingStopClick);
            _recorder = null;
        }
        RefreshHooks();
        RecordingStateChanged?.Invoke(false);
        return steps;
    }

    // ---------------------------------------------------------------- haki

    private void RefreshHooks()
    {
        bool need;
        lock (_lock)
        {
            need = !_disposed && _started && (_recorder != null || _snapshot.ByTrigger.Count > 0);
        }

        if (need) { if (!_source.IsRunning) _source.Start(Intercept); }
        else if (_source.IsRunning) _source.Stop();
    }

    /// <summary>Wywoływane na wątku haka - tylko szybkie operacje, bez blokowania i bez wyjątków.</summary>
    internal bool Intercept(InputEvent ev)
    {
        if (ev.IsOwn) return false;   // własne zdarzenia (odtwarzanie makra) nie wyzwalają niczego i nie są nagrywane

        var rec = _recorder;
        if (rec != null)
        {
            Record(rec, ev);
            return false;
        }

        if (ev.Kind == InputEventKind.Key && ev.Code == PanicKey && ev.IsDown && _engine.AnyRunning)
        {
            _engine.StopAll();
            return false;
        }

        if (ev.IsInjected) return false;

        var snap = _snapshot;
        if (snap.ByTrigger.Count == 0) return false;

        (MacroTriggerKind, int)? trigger = ev.Kind switch
        {
            InputEventKind.Key => (MacroTriggerKind.Key, ev.Code),
            InputEventKind.MouseButton => (MacroTriggerKind.MouseButton, ev.Code),
            InputEventKind.HorizontalWheel when ev.Delta < 0 => (MacroTriggerKind.WheelTiltLeft, 0),
            InputEventKind.HorizontalWheel when ev.Delta > 0 => (MacroTriggerKind.WheelTiltRight, 0),
            _ => null
        };
        if (trigger == null || !snap.ByTrigger.TryGetValue(trigger.Value, out var bindings)) return false;

        bool isMouse = trigger.Value.Item1 != MacroTriggerKind.Key;
        MacroDefinition? macro = null;
        foreach (var b in bindings)
        {
            if (!b.Enabled) continue;
            if (isMouse && b.OnlyRazerMouse && !ev.FromRazer) continue;
            if (snap.Macros.TryGetValue(b.MacroId, out var m)) { macro = m; break; }
        }
        if (macro == null) return false;

        bool tilt = trigger.Value.Item1 is MacroTriggerKind.WheelTiltLeft or MacroTriggerKind.WheelTiltRight;
        if (tilt)
        {
            // przechył kółka nie ma "puszczenia" - jedno uruchomienie (jak naciśnięcie i puszczenie)
            _engine.TriggerDown(macro);
            _engine.TriggerUp(macro);
            return true;
        }

        lock (_heldTriggers)
        {
            if (ev.IsDown)
            {
                if (!_heldTriggers.Add(trigger.Value)) return true;   // autopowtarzanie klawisza - połykamy, nie uruchamiamy ponownie
                _engine.TriggerDown(macro);
                return true;
            }

            if (_heldTriggers.Remove(trigger.Value))
            {
                _engine.TriggerUp(macro);
                return true;      // połykamy też zwolnienie, żeby aplikacja nie dostała "osieroconego" up
            }
        }
        return false;
    }

    private void Record(MacroRecorder rec, InputEvent ev)
    {
        switch (ev.Kind)
        {
            case InputEventKind.Key: rec.OnKey(ev.Code, ev.IsDown, ev.Extended); break;
            case InputEventKind.MouseButton: rec.OnMouseButton((MacroMouseButton)ev.Code, ev.IsDown); break;
            case InputEventKind.Wheel: rec.OnWheel(ev.Delta, horizontal: false); break;
            case InputEventKind.HorizontalWheel: rec.OnWheel(ev.Delta, horizontal: true); break;
        }
        RecordedStepCountChanged?.Invoke(rec.Count);
    }

    // ---------------------------------------------------------------- pomocnicze

    private void PersistAndRebuild()
    {
        RebuildSnapshot();
        try { _store.Save(_library); } catch { /* zapis nie może zablokować działania makr */ }
    }

    private void RebuildSnapshot()
    {
        lock (_heldTriggers) _heldTriggers.Clear();   // zmiana przypisań = czysty stan wyzwalaczy
        var macros = _library.Macros.ToDictionary(m => m.Id, m => m.Clone());
        var byTrigger = new Dictionary<(MacroTriggerKind, int), List<MacroBinding>>();
        foreach (var b in _library.Bindings.Where(b => b.Enabled && macros.ContainsKey(b.MacroId)))
        {
            int code = b.TriggerKind is MacroTriggerKind.WheelTiltLeft or MacroTriggerKind.WheelTiltRight ? 0 : b.Code;
            var key = (b.TriggerKind, code);
            if (!byTrigger.TryGetValue(key, out var list)) byTrigger[key] = list = new List<MacroBinding>();
            list.Add(b);
        }
        _snapshot = new Snapshot(macros, byTrigger);
    }

    private void Changed()
    {
        RefreshHooks();
        LibraryChanged?.Invoke();
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;
        _engine.Dispose();
        _source.Dispose();
    }
}

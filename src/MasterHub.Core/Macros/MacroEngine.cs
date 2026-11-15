using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Macros;

/// <summary>
/// Silnik odtwarzania makr: tryby (Once / WhilePressed / Toggle / RepeatCount), anulowanie oraz ochrona przed
/// "przyklejonymi" klawiszami - wszystko, co makro wcisnęło, a nie zdążyło zwolnić (np. przerwane w połowie), jest
/// zwalniane na końcu przebiegu.
/// </summary>
public sealed class MacroEngine : IDisposable
{
    /// <summary>Minimalna przerwa między przebiegami pętli bez własnych opóźnień (chroni CPU przed pętlą 100%).</summary>
    public const int MinLoopGapMs = 5;
    public const int MaxRepeatCount = 10000;

    private sealed class Run
    {
        public CancellationTokenSource Cts { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;
    }

    private readonly IInputSimulator _sim;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly ConcurrentDictionary<string, Run> _runs = new();
    private readonly object _startLock = new();
    private bool _disposed;

    public event Action<string, bool>? RunningChanged;

    public MacroEngine(IInputSimulator simulator, Func<int, CancellationToken, Task>? delay = null)
    {
        _sim = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _delay = delay ?? ((ms, ct) => ms <= 0 ? Task.CompletedTask : Task.Delay(ms, ct));
    }

    public bool IsRunning(string macroId) => _runs.ContainsKey(macroId);
    public bool AnyRunning => !_runs.IsEmpty;

    /// <summary>Zadanie aktualnie działającego makra (do oczekiwania w testach); null gdy nie działa.</summary>
    public Task? GetRunTask(string macroId) => _runs.TryGetValue(macroId, out var r) ? r.Task : null;

    /// <summary>Wyzwalacz wciśnięty (przycisk myszy / klawisz). Zachowanie zależy od trybu makra.</summary>
    public void TriggerDown(MacroDefinition macro)
    {
        switch (macro.Mode)
        {
            case MacroPlaybackMode.Toggle:
                if (IsRunning(macro.Id)) Stop(macro.Id); else Start(macro);
                break;
            default:   // Once, WhilePressed, RepeatCount: start, jeśli jeszcze nie działa
                Start(macro);
                break;
        }
    }

    /// <summary>Wyzwalacz zwolniony. Tylko tryb WhilePressed reaguje (zatrzymanie pętli).</summary>
    public void TriggerUp(MacroDefinition macro)
    {
        if (macro.Mode == MacroPlaybackMode.WhilePressed) Stop(macro.Id);
    }

    /// <summary>
    /// Uruchomienie z klawisza decka (brak informacji o przytrzymaniu): pętle (WhilePressed / Toggle) działają jak
    /// przełącznik - kolejne wywołanie zatrzymuje.
    /// </summary>
    public void PlayFromDeck(MacroDefinition macro)
    {
        bool loops = macro.Mode is MacroPlaybackMode.WhilePressed or MacroPlaybackMode.Toggle;
        if (loops && IsRunning(macro.Id)) Stop(macro.Id);
        else Start(macro);
    }

    public bool Start(MacroDefinition macro)
    {
        if (_disposed || macro.Steps.Count == 0) return false;
        Run run;
        lock (_startLock)
        {
            if (_runs.ContainsKey(macro.Id)) return false;
            run = new Run();
            _runs[macro.Id] = run;
        }

        RunningChanged?.Invoke(macro.Id, true);
        // Kopia definicji: edycja w UI w trakcie odtwarzania nie może zmieniać wykonywanej sekwencji.
        var snapshot = macro.Clone();
        run.Task = Task.Run(async () =>
        {
            try { await RunLoopAsync(snapshot, run.Cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { /* błąd symulatora nie może zabić procesu */ }
            finally
            {
                _runs.TryRemove(snapshot.Id, out _);
                run.Cts.Dispose();
                RunningChanged?.Invoke(snapshot.Id, false);
            }
        });
        return true;
    }

    public void Stop(string macroId)
    {
        if (_runs.TryGetValue(macroId, out var run))
        {
            try { run.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    public void StopAll()
    {
        foreach (var id in _runs.Keys) Stop(id);
    }

    /// <summary>Wykonuje makro synchronicznie względem wywołującego (pętle zgodnie z trybem) - używane przez testy.</summary>
    public async Task RunLoopAsync(MacroDefinition macro, CancellationToken ct)
    {
        int total = macro.Mode switch
        {
            MacroPlaybackMode.Once => 1,
            MacroPlaybackMode.RepeatCount => Math.Clamp(macro.RepeatCount, 1, MaxRepeatCount),
            _ => int.MaxValue
        };

        bool hasOwnDelays = macro.Steps.Exists(s => s.Type == MacroStepType.Delay && s.DelayMs > 0);
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            await PassAsync(macro, ct).ConfigureAwait(false);

            if (i + 1 < total)
            {
                int gap = Math.Max(0, macro.RepeatDelayMs);
                if (gap == 0 && !hasOwnDelays) gap = MinLoopGapMs;
                await _delay(gap, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task PassAsync(MacroDefinition macro, CancellationToken ct)
    {
        var heldKeys = new List<(int Vk, bool Ext)>();
        var heldButtons = new HashSet<MacroMouseButton>();
        try
        {
            foreach (var step in macro.Steps)
            {
                ct.ThrowIfCancellationRequested();
                switch (step.Type)
                {
                    case MacroStepType.KeyDown:
                        if (!heldKeys.Contains((step.Key, step.Extended)))
                        {
                            _sim.KeyDown(step.Key, step.Extended);
                            heldKeys.Add((step.Key, step.Extended));
                        }
                        break;
                    case MacroStepType.KeyUp:
                        _sim.KeyUp(step.Key, step.Extended);
                        heldKeys.Remove((step.Key, step.Extended));
                        break;
                    case MacroStepType.MouseDown:
                        if (heldButtons.Add(step.Button)) _sim.MouseDown(step.Button);
                        break;
                    case MacroStepType.MouseUp:
                        _sim.MouseUp(step.Button);
                        heldButtons.Remove(step.Button);
                        break;
                    case MacroStepType.Wheel:
                        _sim.Wheel(step.Delta, step.Horizontal);
                        break;
                    case MacroStepType.Text:
                        if (!string.IsNullOrEmpty(step.Text)) _sim.TypeText(step.Text);
                        break;
                    case MacroStepType.Delay:
                        await _delay(Math.Clamp(step.DelayMs, 0, 600_000), ct).ConfigureAwait(false);
                        break;
                }
            }
        }
        finally
        {
            // Ochrona przed przyklejonymi klawiszami/przyciskami po anulowaniu lub błędzie (zwalniamy w odwrotnej kolejności).
            for (int i = heldKeys.Count - 1; i >= 0; i--)
            {
                try { _sim.KeyUp(heldKeys[i].Vk, heldKeys[i].Ext); } catch { }
            }
            foreach (var b in heldButtons)
            {
                try { _sim.MouseUp(b); } catch { }
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        StopAll();
    }
}

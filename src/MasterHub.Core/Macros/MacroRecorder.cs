using System;
using System.Collections.Generic;

namespace MasterHub.Core.Macros;

/// <summary>
/// Zamienia strumień zdarzeń wejścia na kroki makra. Eliminuje autopowtarzanie klawiszy i osierocone zwolnienia
/// (np. puszczenie przycisku myszy, którym kliknięto "Nagrywaj"), a opóźnienia wylicza wg <see cref="MacroDelayMode"/>.
/// </summary>
public sealed class MacroRecorder
{
    private readonly MacroDelayMode _mode;
    private readonly int _fixedDelayMs;
    private readonly Func<long> _nowMs;
    private readonly List<MacroStep> _steps = new();
    private readonly HashSet<(int Vk, bool Ext)> _downKeys = new();
    private readonly HashSet<MacroMouseButton> _downButtons = new();
    private long _lastEventMs = -1;

    public MacroRecorder(MacroDelayMode mode, int fixedDelayMs = 50, Func<long>? nowMs = null)
    {
        _mode = mode;
        _fixedDelayMs = Math.Clamp(fixedDelayMs, 1, 60_000);
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    public int Count => _steps.Count;
    public IReadOnlyList<MacroStep> Steps => _steps;

    public void OnKey(int vk, bool isDown, bool extended = false)
    {
        var id = (vk, extended);
        if (isDown)
        {
            if (!_downKeys.Add(id)) return;            // autopowtarzanie klawisza
            Append(MacroStep.KeyDown(vk, extended));
        }
        else
        {
            if (!_downKeys.Remove(id)) return;         // zwolnienie bez wcisnięcia (np. początek nagrywania)
            Append(MacroStep.KeyUp(vk, extended));
        }
    }

    public void OnMouseButton(MacroMouseButton button, bool isDown)
    {
        if (isDown)
        {
            if (!_downButtons.Add(button)) return;
            Append(MacroStep.MouseDown(button));
        }
        else
        {
            if (!_downButtons.Remove(button)) return;  // np. zwolnienie kliknięcia, które uruchomiło nagrywanie
            Append(MacroStep.MouseUp(button));
        }
    }

    public void OnWheel(int delta, bool horizontal)
    {
        if (delta == 0) return;
        Append(MacroStep.Wheel(delta, horizontal));
    }

    private void Append(MacroStep step)
    {
        long now = _nowMs();
        if (_lastEventMs >= 0 && _steps.Count > 0)
        {
            int gap = _mode switch
            {
                MacroDelayMode.Recorded => (int)Math.Clamp(now - _lastEventMs, 0, 600_000),
                MacroDelayMode.Fixed => _fixedDelayMs,
                _ => 0
            };
            if (gap > 0) _steps.Add(MacroStep.Delay(gap));
        }
        _lastEventMs = now;
        _steps.Add(step);
    }

    /// <summary>
    /// Kończy nagrywanie: zwalnia jeszcze wciśnięte klawisze/przyciski (makro nie może zostawić ich "wciśniętych")
    /// i opcjonalnie odcina końcowe kliknięcie lewym przyciskiem (kliknięcie "Zatrzymaj" w interfejsie).
    /// </summary>
    public List<MacroStep> Build(bool trimTrailingStopClick = true)
    {
        var result = new List<MacroStep>(_steps);

        if (trimTrailingStopClick)
        {
            // [... Delay? MouseDown(L) Delay? MouseUp(L)] albo tylko [.. MouseDown(L)] na końcu
            TrimTrailingLeftClick(result);
        }

        foreach (var (vk, ext) in _downKeys)
        {
            if (!HasTrailingRelease(result, step => step.Type == MacroStepType.KeyUp && step.Key == vk && step.Extended == ext))
                result.Add(MacroStep.KeyUp(vk, ext));
        }
        foreach (var b in _downButtons)
        {
            if (!HasTrailingRelease(result, step => step.Type == MacroStepType.MouseUp && step.Button == b))
                result.Add(MacroStep.MouseUp(b));
        }

        while (result.Count > 0 && result[^1].Type == MacroStepType.Delay) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static bool HasTrailingRelease(List<MacroStep> steps, Func<MacroStep, bool> isRelease)
    {
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            if (isRelease(steps[i])) return true;
            if (steps[i].Type != MacroStepType.Delay) return false;
        }
        return false;
    }

    private void TrimTrailingLeftClick(List<MacroStep> steps)
    {
        int i = steps.Count - 1;
        while (i >= 0 && steps[i].Type == MacroStepType.Delay) i--;
        if (i < 0) return;

        if (steps[i].Type == MacroStepType.MouseUp && steps[i].Button == MacroMouseButton.Left)
        {
            // kompletne kliknięcie: [MouseDown(L) Delay? MouseUp(L)] na końcu
            int j = i - 1;
            while (j >= 0 && steps[j].Type == MacroStepType.Delay) j--;
            if (j >= 0 && steps[j].Type == MacroStepType.MouseDown && steps[j].Button == MacroMouseButton.Left)
            {
                steps.RemoveRange(j, steps.Count - j);
            }
        }
        else if (steps[i].Type == MacroStepType.MouseDown && steps[i].Button == MacroMouseButton.Left)
        {
            // kliknięcie "Zatrzymaj" bez zwolnienia - nie zostawiamy przycisku wciśniętego
            steps.RemoveRange(i, steps.Count - i);
            _downButtons.Remove(MacroMouseButton.Left);
        }
    }
}

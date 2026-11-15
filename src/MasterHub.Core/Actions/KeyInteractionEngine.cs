using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Actions;

/// <summary>
/// Domyślna, wielowątkowo bezpieczna implementacja silnika interakcji klawiszy MasterHub.
/// Obsługuje timery przytrzymania (Hold) i okno podwójnego kliknięcia (Double Press) zgodnie z Etapem 1.3.
/// </summary>
public sealed class KeyInteractionEngine : IKeyInteractionEngine
{
    private readonly object _syncRoot = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _holdTokens = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastPressTimes = new();
    private readonly ConcurrentDictionary<int, bool> _physicalPressStates = new();

    public int HoldDurationMs { get; set; } = 500;
    public int DoublePressIntervalMs { get; set; } = 300;
    public int HoldRepeatInitialDelayMs { get; set; } = 400;
    public int HoldRepeatIntervalMs { get; set; } = 100;

    public event Action<int, KeyPressMode>? KeyTriggered;
    public event Action<int, KeyGesture>? GestureTriggered;

    private readonly Func<long> _nowMs;
    private readonly GestureRecognizer _gestures = new();
    private Timer? _gestureTimer;

    /// <param name="nowMs">Zegar w milisekundach (domyślnie monotoniczny); wstrzykiwany w testach.</param>
    public KeyInteractionEngine(Func<long>? nowMs = null)
    {
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    public void HandleKeyEvent(int keyIndex, bool isPressed, KeyPressMode mode, int? holdDurationMs = null)
    {
        lock (_syncRoot)
        {
            bool previousState = _physicalPressStates.TryGetValue(keyIndex, out bool s) && s;
            _physicalPressStates[keyIndex] = isPressed;

            switch (mode)
            {
                case KeyPressMode.OnPress:
                    if (isPressed && !previousState)
                    {
                        Trigger(keyIndex, KeyPressMode.OnPress);
                    }
                    break;

                case KeyPressMode.OnRelease:
                    if (!isPressed && previousState)
                    {
                        Trigger(keyIndex, KeyPressMode.OnRelease);
                    }
                    break;

                case KeyPressMode.OnHold:
                    if (isPressed && !previousState)
                    {
                        CancelHoldTimer(keyIndex);

                        var cts = new CancellationTokenSource();
                        var token = cts.Token;
                        _holdTokens[keyIndex] = cts;
                        int holdDuration = holdDurationMs ?? HoldDurationMs;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(holdDuration, token).ConfigureAwait(false);
                                lock (_syncRoot)
                                {
                                    if (_physicalPressStates.TryGetValue(keyIndex, out bool pressed) && pressed)
                                    {
                                        Trigger(keyIndex, KeyPressMode.OnHold);
                                    }
                                }
                            }
                            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                            {
                                // Klawisz puszczony przed upływem HoldDurationMs - zamierzona anulacja
                            }
                        }, token);
                    }
                    else if (!isPressed)
                    {
                        CancelHoldTimer(keyIndex);
                    }
                    break;

                case KeyPressMode.OnDoublePress:
                    if (isPressed && !previousState)
                    {
                        var now = DateTime.UtcNow;
                        if (_lastPressTimes.TryGetValue(keyIndex, out var lastTime) &&
                            (now - lastTime).TotalMilliseconds <= DoublePressIntervalMs)
                        {
                            _lastPressTimes[keyIndex] = DateTime.MinValue; // reset
                            Trigger(keyIndex, KeyPressMode.OnDoublePress);
                        }
                        else
                        {
                            _lastPressTimes[keyIndex] = now;
                        }
                    }
                    break;

                case KeyPressMode.OnHoldRepeat:
                    if (isPressed && !previousState)
                    {
                        CancelHoldTimer(keyIndex);

                        // Pierwsze wyzwolenie następuje natychmiast po naciśnięciu
                        Trigger(keyIndex, KeyPressMode.OnHoldRepeat);

                        var cts = new CancellationTokenSource();
                        var token = cts.Token;
                        _holdTokens[keyIndex] = cts;
                        int initialDelay = HoldRepeatInitialDelayMs;
                        int repeatInterval = HoldRepeatIntervalMs;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(initialDelay, token).ConfigureAwait(false);

                                while (!token.IsCancellationRequested)
                                {
                                    lock (_syncRoot)
                                    {
                                        if (!_physicalPressStates.TryGetValue(keyIndex, out bool pressed) || !pressed)
                                        {
                                            break;
                                        }
                                        Trigger(keyIndex, KeyPressMode.OnHoldRepeat);
                                    }

                                    await Task.Delay(repeatInterval, token).ConfigureAwait(false);
                                }
                            }
                            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                            {
                                // Klawisz zwolniony - zamierzona anulacja pętli powtarzania
                            }
                        }, token);
                    }
                    else if (!isPressed)
                    {
                        CancelHoldTimer(keyIndex);
                    }
                    break;
            }
        }
    }

    public void HandleGestureEvent(int keyIndex, bool isPressed, GestureBindings bound)
    {
        var fired = new List<KeyGesture>();
        lock (_syncRoot)
        {
            _gestures.HoldMs = HoldDurationMs;
            _gestures.DoubleMs = DoublePressIntervalMs;
            fired.AddRange(_gestures.OnKey(keyIndex, isPressed, bound, _nowMs()));
            ScheduleGestureTimer();
        }

        foreach (var gesture in fired) RaiseGesture(keyIndex, gesture);
    }

    // One timer for every key: it is armed for the nearest hold / double-tap deadline and re-armed after each tick.
    private void ScheduleGestureTimer()
    {
        long? deadline = _gestures.NextDeadline;
        if (deadline == null)
        {
            _gestureTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        int delay = (int)Math.Clamp(deadline.Value - _nowMs(), 1, int.MaxValue);
        _gestureTimer ??= new Timer(_ => OnGestureTimer(), null, Timeout.Infinite, Timeout.Infinite);
        _gestureTimer.Change(delay, Timeout.Infinite);
    }

    private void OnGestureTimer()
    {
        IReadOnlyList<(int Key, KeyGesture Gesture)> fired;
        lock (_syncRoot)
        {
            fired = _gestures.Tick(_nowMs());
            ScheduleGestureTimer();
        }

        foreach (var (key, gesture) in fired) RaiseGesture(key, gesture);
    }

    private void RaiseGesture(int keyIndex, KeyGesture gesture)
    {
        try
        {
            GestureTriggered?.Invoke(keyIndex, gesture);
        }
        catch
        {
            // a throwing handler must not break the recogniser or its timer
        }
    }

    private void CancelHoldTimer(int keyIndex)
    {
        if (_holdTokens.TryRemove(keyIndex, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
    }

    private void Trigger(int keyIndex, KeyPressMode mode)
    {
        try
        {
            KeyTriggered?.Invoke(keyIndex, mode);
        }
        catch
        {
            // Wyjątek w obsłudze akcji nie może przerwać pętli powtarzania ani zostawić blokady _syncRoot.
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            foreach (var key in _holdTokens.Keys)
            {
                CancelHoldTimer(key);
            }
            _holdTokens.Clear();
            _lastPressTimes.Clear();
            _gestures.Reset();
            _gestureTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _physicalPressStates.Clear();
        }
    }

    public void Dispose()
    {
        Reset();
        _gestureTimer?.Dispose();
        _gestureTimer = null;
    }
}

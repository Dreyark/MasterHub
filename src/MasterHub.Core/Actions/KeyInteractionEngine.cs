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

    public void HandleKeyEvent(int keyIndex, bool isPressed, KeyPressMode mode)
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
                        _holdTokens[keyIndex] = cts;
                        int holdDuration = HoldDurationMs;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(holdDuration, cts.Token).ConfigureAwait(false);
                                lock (_syncRoot)
                                {
                                    if (_physicalPressStates.TryGetValue(keyIndex, out bool pressed) && pressed)
                                    {
                                        Trigger(keyIndex, KeyPressMode.OnHold);
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Klawisz puszczony przed upływem HoldDurationMs - zamierzona anulacja
                            }
                        }, cts.Token);
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
                        _holdTokens[keyIndex] = cts;
                        int initialDelay = HoldRepeatInitialDelayMs;
                        int repeatInterval = HoldRepeatIntervalMs;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(initialDelay, cts.Token).ConfigureAwait(false);

                                while (!cts.Token.IsCancellationRequested)
                                {
                                    lock (_syncRoot)
                                    {
                                        if (!_physicalPressStates.TryGetValue(keyIndex, out bool pressed) || !pressed)
                                        {
                                            break;
                                        }
                                        Trigger(keyIndex, KeyPressMode.OnHoldRepeat);
                                    }

                                    await Task.Delay(repeatInterval, cts.Token).ConfigureAwait(false);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Klawisz zwolniony - zamierzona anulacja pętli powtarzania
                            }
                        }, cts.Token);
                    }
                    else if (!isPressed)
                    {
                        CancelHoldTimer(keyIndex);
                    }
                    break;
            }
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
        KeyTriggered?.Invoke(keyIndex, mode);
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
            _physicalPressStates.Clear();
        }
    }

    public void Dispose()
    {
        Reset();
    }
}

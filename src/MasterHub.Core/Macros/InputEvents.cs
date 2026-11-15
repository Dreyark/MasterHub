using System;

namespace MasterHub.Core.Macros;

public enum InputEventKind
{
    Key,
    MouseButton,
    Wheel,
    HorizontalWheel
}

/// <summary>Zdarzenie wejścia z globalnego haka.</summary>
/// <param name="Code">Key: kod VK; MouseButton: wartość <see cref="MacroMouseButton"/>; Wheel: nieużywane.</param>
/// <param name="Delta">Dla kółek: przesunięcie (120 = jedno kliknięcie).</param>
/// <param name="IsInjected">Zdarzenie wygenerowane programowo (SendInput) przez dowolną aplikację.</param>
/// <param name="IsOwn">Zdarzenie wygenerowane przez MasterHub (znacznik dwExtraInfo).</param>
/// <param name="FromRazer">Ostatnio aktywna mysz (Raw Input) to urządzenie Razer (VID 0x1532).</param>
public readonly record struct InputEvent(
    InputEventKind Kind,
    int Code,
    bool IsDown,
    int Delta,
    bool Extended,
    bool IsInjected,
    bool IsOwn,
    bool FromRazer);

/// <summary>
/// Źródło zdarzeń wejścia. Odbiorca zwraca true, jeśli zdarzenie ma zostać "połknięte" (nie dotrze do aplikacji).
/// Wywołanie odbywa się na wątku haka - musi być bardzo szybkie (system ma limit ok. 300 ms na odpowiedź haka).
/// </summary>
public interface IInputEventSource : IDisposable
{
    bool IsRunning { get; }
    void Start(Func<InputEvent, bool> intercept);
    void Stop();
}

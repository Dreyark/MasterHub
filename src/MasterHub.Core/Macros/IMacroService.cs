using System;
using System.Collections.Generic;

namespace MasterHub.Core.Macros;

/// <summary>
/// Makra myszy/klawiatury (odpowiednik makr Razer Synapse, działający programowo): biblioteka makr, przypisania do
/// przycisków myszy (np. bocznych X1/X2 myszy Razer) lub klawiszy, nagrywanie i odtwarzanie.
/// Makra działają, gdy MasterHub jest uruchomiony (także w zasobniku).
/// </summary>
public interface IMacroService : IDisposable
{
    IReadOnlyList<MacroDefinition> Macros { get; }
    IReadOnlyList<MacroBinding> Bindings { get; }
    event Action? LibraryChanged;
    event Action<string, bool>? MacroRunningChanged;

    bool IsRecording { get; }
    event Action<bool>? RecordingStateChanged;
    event Action<int>? RecordedStepCountChanged;

    /// <summary>Wczytuje bibliotekę i włącza nasłuch tylko jeśli są aktywne przypisania.</summary>
    void Start();

    MacroDefinition? GetMacro(string id);
    bool IsRunning(string macroId);

    /// <summary>Dodaje lub podmienia makro (po Id).</summary>
    void SaveMacro(MacroDefinition macro);

    /// <summary>Usuwa makro razem z jego przypisaniami.</summary>
    void DeleteMacro(string id);

    void SaveBinding(MacroBinding binding);
    void DeleteBinding(string id);

    /// <summary>Uruchamia makro (np. z klawisza decka). Pętle działają jak przełącznik.</summary>
    void Play(string macroId);
    void Stop(string macroId);
    void StopAll();

    void StartRecording(MacroDelayMode mode, int fixedDelayMs = 50);

    /// <param name="trimTrailingStopClick">Odetnij końcowe kliknięcie lewym przyciskiem (kliknięcie przycisku "Zatrzymaj").</param>
    List<MacroStep> StopRecording(bool trimTrailingStopClick = true);
}

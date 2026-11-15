using System;
using System.Collections.Generic;

namespace MasterHub.Core.Macros;

/// <summary>Rodzaj kroku makra.</summary>
public enum MacroStepType
{
    KeyDown = 0,
    KeyUp = 1,
    MouseDown = 2,
    MouseUp = 3,
    Wheel = 4,
    Delay = 5,
    Text = 6
}

public enum MacroMouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    X1 = 3,
    X2 = 4
}

/// <summary>Sposób odtwarzania makra (jak w Razer Synapse).</summary>
public enum MacroPlaybackMode
{
    /// <summary>Raz po naciśnięciu przycisku.</summary>
    Once = 0,
    /// <summary>Powtarzaj, dopóki przycisk jest przytrzymany.</summary>
    WhilePressed = 1,
    /// <summary>Pierwsze naciśnięcie uruchamia pętlę, kolejne ją zatrzymuje.</summary>
    Toggle = 2,
    /// <summary>Wykonaj dokładnie N razy.</summary>
    RepeatCount = 3
}

/// <summary>Jak traktować opóźnienia podczas nagrywania.</summary>
public enum MacroDelayMode
{
    /// <summary>Zapisz rzeczywiste odstępy czasowe między zdarzeniami.</summary>
    Recorded = 0,
    /// <summary>Stałe opóźnienie między każdą parą zdarzeń.</summary>
    Fixed = 1,
    /// <summary>Bez opóźnień (zdarzenia jedno po drugim).</summary>
    None = 2
}

public class MacroStep
{
    public MacroStepType Type { get; set; }

    /// <summary>Kod wirtualnego klawisza (VK_*) dla KeyDown / KeyUp.</summary>
    public int Key { get; set; }

    /// <summary>Klawisz rozszerzony (strzałki, Insert, Delete, Home, End, PgUp/PgDn, prawy Ctrl/Alt, klawisze numeryczne).</summary>
    public bool Extended { get; set; }

    public MacroMouseButton Button { get; set; }

    /// <summary>Przesunięcie kółka (120 = jedno "kliknięcie"); dla Wheel.</summary>
    public int Delta { get; set; }

    /// <summary>true = kółko poziome (przechył).</summary>
    public bool Horizontal { get; set; }

    /// <summary>Czas w ms dla Delay.</summary>
    public int DelayMs { get; set; }

    /// <summary>Tekst do wpisania (Unicode) dla Text.</summary>
    public string? Text { get; set; }

    public MacroStep Clone() => (MacroStep)MemberwiseClone();

    public static MacroStep Delay(int ms) => new() { Type = MacroStepType.Delay, DelayMs = Math.Max(0, ms) };
    public static MacroStep KeyDown(int vk, bool extended = false) => new() { Type = MacroStepType.KeyDown, Key = vk, Extended = extended };
    public static MacroStep KeyUp(int vk, bool extended = false) => new() { Type = MacroStepType.KeyUp, Key = vk, Extended = extended };
    public static MacroStep MouseDown(MacroMouseButton b) => new() { Type = MacroStepType.MouseDown, Button = b };
    public static MacroStep MouseUp(MacroMouseButton b) => new() { Type = MacroStepType.MouseUp, Button = b };
    public static MacroStep Wheel(int delta, bool horizontal = false) => new() { Type = MacroStepType.Wheel, Delta = delta, Horizontal = horizontal };
    public static MacroStep TypeText(string text) => new() { Type = MacroStepType.Text, Text = text };
}

public class MacroDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Nowe makro";
    public List<MacroStep> Steps { get; set; } = new();
    public MacroPlaybackMode Mode { get; set; } = MacroPlaybackMode.Once;

    /// <summary>Liczba powtórzeń dla trybu RepeatCount (1..10000).</summary>
    public int RepeatCount { get; set; } = 1;

    /// <summary>Przerwa między kolejnymi przebiegami w trybach powtarzania (ms).</summary>
    public int RepeatDelayMs { get; set; }

    public MacroDefinition Clone()
    {
        var c = (MacroDefinition)MemberwiseClone();
        c.Steps = Steps.ConvertAll(s => s.Clone());
        return c;
    }
}

public enum MacroTriggerKind
{
    /// <summary>Przycisk myszy (Środkowy, X1, X2).</summary>
    MouseButton = 0,
    /// <summary>Dowolny klawisz klawiatury (np. F13-F24 zmapowane z przycisków myszy w Synapse).</summary>
    Key = 1,
    WheelTiltLeft = 2,
    WheelTiltRight = 3
}

/// <summary>Przypisanie makra do wyzwalacza (przycisku myszy lub klawisza).</summary>
public class MacroBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public MacroTriggerKind TriggerKind { get; set; } = MacroTriggerKind.MouseButton;

    /// <summary>Dla MouseButton: wartość <see cref="MacroMouseButton"/>; dla Key: kod VK.</summary>
    public int Code { get; set; } = (int)MacroMouseButton.X1;

    public string MacroId { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Reaguj tylko na zdarzenia z myszy Razer (VID 0x1532, rozpoznawane przez Raw Input). Dotyczy wyzwalaczy myszy.
    /// Gdy false - dowolna mysz.
    /// </summary>
    public bool OnlyRazerMouse { get; set; } = true;
}

/// <summary>Zawartość pliku macros.json.</summary>
public class MacroLibrary
{
    public int Version { get; set; } = 1;
    public List<MacroDefinition> Macros { get; set; } = new();
    public List<MacroBinding> Bindings { get; set; } = new();
}

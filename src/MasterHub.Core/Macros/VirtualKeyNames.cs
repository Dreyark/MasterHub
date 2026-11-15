using System;
using System.Collections.Generic;

namespace MasterHub.Core.Macros;

/// <summary>Czytelne nazwy kodów VK_* do wyświetlania w edytorze makr oraz klasyfikacja klawiszy rozszerzonych.</summary>
public static class VirtualKeyNames
{
    private static readonly Dictionary<int, string> Special = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt",
        [0x13] = "Pause", [0x14] = "Caps Lock", [0x1B] = "Esc", [0x20] = "Space", [0x21] = "Page Up", [0x22] = "Page Down",
        [0x23] = "End", [0x24] = "Home", [0x25] = "←", [0x26] = "↑", [0x27] = "→", [0x28] = "↓", [0x2C] = "Print Screen",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x5B] = "Win", [0x5C] = "Win (P)", [0x5D] = "Menu", [0x60] = "Num 0", [0x61] = "Num 1",
        [0x62] = "Num 2", [0x63] = "Num 3", [0x64] = "Num 4", [0x65] = "Num 5", [0x66] = "Num 6", [0x67] = "Num 7", [0x68] = "Num 8",
        [0x69] = "Num 9", [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /", [0x90] = "Num Lock",
        [0x91] = "Scroll Lock", [0xA0] = "Shift (L)", [0xA1] = "Shift (R)", [0xA2] = "Ctrl (L)", [0xA3] = "Ctrl (R)", [0xA4] = "Alt (L)",
        [0xA5] = "Alt (R)", [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'"
    };

    public static string Get(int vk)
    {
        if (Special.TryGetValue(vk, out var s)) return s;
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        return $"VK 0x{vk:X2}";
    }

    /// <summary>Czy klawisz wymaga flagi KEYEVENTF_EXTENDEDKEY przy symulacji (strzałki, Insert/Delete/Home/End/PgUp/PgDn, prawy Ctrl/Alt itd.).</summary>
    public static bool IsExtended(int vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2C or 0x2D or 0x2E or
        0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;

    public static string Describe(MacroStep step) => step.Type switch
    {
        MacroStepType.KeyDown => $"⬇ {Get(step.Key)}",
        MacroStepType.KeyUp => $"⬆ {Get(step.Key)}",
        MacroStepType.MouseDown => $"🖱 ⬇ {step.Button}",
        MacroStepType.MouseUp => $"🖱 ⬆ {step.Button}",
        MacroStepType.Wheel => $"🖱 {(step.Horizontal ? "↔" : "↕")} {step.Delta}",
        MacroStepType.Delay => $"⏱ {step.DelayMs} ms",
        MacroStepType.Text => $"⌨ \"{step.Text}\"",
        _ => step.Type.ToString()
    };
}

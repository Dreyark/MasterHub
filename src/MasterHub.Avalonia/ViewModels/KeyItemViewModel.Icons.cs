using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Localization;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels;

public partial class KeyItemViewModel
{

    public record IconPreset(string Glyph, string Label);
    public record IconCategory(string CategoryName, System.Collections.Generic.IReadOnlyList<IconPreset> Icons);

    public static readonly System.Collections.Generic.IReadOnlyList<string> AvailableFontFamilies = GetAvailableFontFamilies();

    private static string[] GetAvailableFontFamilies()
    {
        var curated = new List<string>
        {
            "Segoe UI",
            "Arial",
            "Bahnschrift",
            "Calibri",
            "Cascadia Code",
            "Cascadia Mono",
            "Century Gothic",
            "Comic Sans MS",
            "Consolas",
            "Courier New",
            "Franklin Gothic Medium",
            "Georgia",
            "Impact",
            "Lucida Console",
            "Lucida Sans Unicode",
            "Segoe UI Black",
            "Segoe UI Semibold",
            "Segoe UI Variable Display",
            "Tahoma",
            "Times New Roman",
            "Trebuchet MS",
            "Verdana"
        };

        try
        {
            var systemFonts = SkiaSharp.SKFontManager.Default.GetFontFamilies();
            if (systemFonts != null)
            {
                foreach (var font in systemFonts)
                {
                    if (!string.IsNullOrWhiteSpace(font) && !font.StartsWith('@') && !curated.Contains(font, StringComparer.OrdinalIgnoreCase))
                    {
                        curated.Add(font);
                    }
                }
            }
        }
        catch { }

        return curated.ToArray();
    }

    public static readonly System.Collections.Generic.IReadOnlyList<IconCategory> CategorizedIcons = new IconCategory[]
    {
        new("Znaki i Symbole", new IconPreset[]
        {
            new("★", "Gwiazda pełna"), new("☆", "Gwiazda pusta"), new("✦", "Błysk 1"), new("✧", "Błysk 2"),
            new("◆", "Romb pełny"), new("◇", "Romb pusty"), new("▲", "Trójkąt góra"), new("▼", "Trójkąt dół"),
            new("◄", "Trójkąt lewo"), new("►", "Trójkąt prawo"), new("■", "Kwadrat"), new("□", "Kwadrat pusty"),
            new("●", "Kółko pełne"), new("○", "Kółko puste"), new("✓", "Zatwierdź"), new("✗", "Błąd"),
            new("✕", "Anuluj"), new("✚", "Plus"), new("✱", "Gwiazdka"), new("⚙", "Zębatka"),
            new("⚡", "Piorun"), new("♾", "Nieskończoność"), new("⚑", "Flaga"), new("⚓", "Kotwica"),
            new("⚔", "Miecze"), new("🛡", "Tarcza"), new("⚜", "Lilijka"), new("⚠", "Uwaga"), new("⛔", "Stop")
        }),
        new("Strzałki i Kierunki", new IconPreset[]
        {
            new("↑", "Góra"), new("↓", "Dół"), new("←", "Lewo"), new("→", "Prawo"),
            new("↔", "Poziomo"), new("↕", "Pionowo"), new("↖", "Góra-Lewo"), new("↗", "Góra-Prawo"),
            new("↘", "Dół-Prawo"), new("↙", "Dół-Lewo"), new("↺", "Cofnij"), new("↻", "Ponów"),
            new("↶", "Skręt lewo"), new("↷", "Skręt prawo"), new("⇒", "Podwójna w prawo"), new("⇐", "Podwójna w lewo"),
            new("»", "Szybko w prawo"), new("«", "Szybko w lewo")
        }),
        new("Klawisze i Etykiety", new IconPreset[]
        {
            new("ESC", "Escape"), new("TAB", "Tabulator"), new("CTRL", "Control"), new("ALT", "Alt"),
            new("SHIFT", "Shift"), new("ENTER", "Enter"), new("DEL", "Delete"), new("WIN", "Windows"),
            new("F1", "Klawisz F1"), new("F2", "Klawisz F2"), new("F3", "Klawisz F3"), new("F4", "Klawisz F4"),
            new("F5", "Odśwież F5"), new("F6", "Klawisz F6"), new("F7", "Klawisz F7"), new("F8", "Klawisz F8"),
            new("F9", "Klawisz F9"), new("F10", "Klawisz F10"), new("F11", "Pełny ekran F11"), new("F12", "DevTools F12")
        }),
        new("Cyfry i Indeksy", new IconPreset[]
        {
            new("①", "1"), new("②", "2"), new("③", "3"), new("④", "4"),
            new("⑤", "5"), new("⑥", "6"), new("⑦", "7"), new("⑧", "8"),
            new("⑨", "9"), new("⓪", "0"), new("❶", "1 ciemny"), new("❷", "2 ciemny"),
            new("❸", "3 ciemny"), new("❹", "4 ciemny"), new("❺", "5 ciemny"), new("❻", "6 ciemny")
        }),
        new("Multimedia", new IconPreset[]
        {
            new("▶", "Odtwórz"), new("⏸", "Wstrzymaj"), new("▶⏸", "Play/Pause"), new("■", "Stop"),
            new("⏮", "Poprzedni"), new("⏭", "Następny"), new("⏪", "Przewiń tył"), new("⏩", "Przewiń przód"),
            new("🔊", "Głośniej"), new("🔉", "Ciszej"), new("🔈", "Cicho"), new("🔇", "Wycisz"),
            new("🎙️", "Mikrofon"), new("🎧", "Słuchawki"), new("🎵", "Nuta"), new("🎶", "Muzyka"),
            new("📻", "Radio"), new("🔔", "Dzwonek"), new("🔕", "Brak dźwięku")
        }),
        new("Sprzęt i Gry", new IconPreset[]
        {
            new("🎯", "DPI / Cel"), new("⚡", "Częstotliwość Hz / Turbo"), new("🐍", "Razer"),
            new("⌨️", "Klawiatura"), new("🖱️", "Mysz"), new("🎮", "Pad / Gra"), new("🕹️", "Dżojstik"),
            new("📊", "Zużycie"), new("🧠", "Pamięć RAM"), new("🌡️", "Temperatura"), new("🚀", "Boost / Launch"),
            new("💻", "Laptop"), new("🖥️", "Monitor / PC"), new("💾", "Dysk / Zapisz"), new("🔋", "Bateria"),
            new("🔌", "Zasilanie"), new("⚙️", "Ustawienia")
        }),
        new("System i Narzędzia", new IconPreset[]
        {
            new("🔒", "Zablokuj"), new("🔓", "Odblokuj"), new("💤", "Uśpij"), new("⏻", "Zasilanie"),
            new("🔄", "Restart"), new("📸", "Screenshot"), new("🧮", "Kalkulator"), new("🌐", "Internet / URL"),
            new("📁", "Folder"), new("📂", "Otwarty folder"), new("📝", "Notatnik"), new("📋", "Wklej tekst"),
            new("🗑️", "Kosz"), new("🔍", "Szukaj"), new("⏱️", "Stoper"), new("⏰", "Budzik"),
            new("📅", "Kalendarz"), new("🛠️", "Narzędzia")
        }),
        new("Transmisja i Czat", new IconPreset[]
        {
            new("📡", "Live / Stream"), new("🔴", "REC / Nagrywanie"), new("📹", "Kamera"), new("🎬", "Scena OBS"),
            new("🎭", "Tryb studyjny"), new("💬", "Czat"), new("🗨️", "Dymek czatu"), new("📞", "Rozmowa"),
            new("✋", "Podnieś rękę"), new("⭐", "Gwiazdka"), new("🌟", "Subskrypcja"), new("💥", "Wybuch"),
            new("💡", "Światło / Pomysł"), new("🛡️", "Ochrona / Mod"), new("👑", "VIP / Korona"), new("🏆", "Puchar")
        })
    };

    public static readonly System.Collections.Generic.IReadOnlyList<IconPreset> PresetIcons = new IconPreset[]
    {
        new("▶⏸", "Play/Pause"), new("⏭", "Następny"), new("⏮", "Poprzedni"), new("■", "Stop"),
        new("🔊", "Głośniej"), new("🔉", "Ciszej"), new("🔇", "Wycisz"), new("🎙️", "Mikrofon"),
        new("★", "Gwiazdka"), new("✓", "Zatwierdź"), new("✕", "Anuluj"), new("⚡", "Błysk"),
        new("↑", "Góra"), new("↓", "Dół"), new("←", "Lewo"), new("→", "Prawo"),
        new("🎯", "DPI"), new("🐍", "Razer"), new("🎮", "Gra"), new("📊", "Zużycie"),
        new("📸", "Zrzut"), new("🌐", "Web"), new("📁", "Folder"), new("🔒", "Blokada"),
        new("💤", "Uśpij"), new("⏻", "Zasilanie"), new("🔄", "Restart"), new("🔴", "REC")
    };

    [RelayCommand]
    public void SetPresetIcon(string glyph)
    {
        if (SelectedEditState == 1)
        {
            ActiveCustomTitle = glyph;
        }
        else
        {
            CustomTitle = glyph;
        }
        OnPropertyChanged(nameof(CurrentEditTitle));
        ActionChangedCallback?.Invoke(this);
    }

    [RelayCommand]
    public void ClearPresetIcon()
    {
        if (SelectedEditState == 1)
        {
            ActiveCustomTitle = "";
        }
        else
        {
            CustomTitle = "";
        }
        OnPropertyChanged(nameof(CurrentEditTitle));
        ActionChangedCallback?.Invoke(this);
    }

    public static System.Collections.Generic.IReadOnlyList<MasterHub.Core.Rendering.ColorStudioHelper.ColorItem> PresetColors =>
        MasterHub.Core.Rendering.ColorStudioHelper.CuratedPalette;

    public void SelectBackgroundColor(string hex)
    {
        if (SelectedEditState == 1)
        {
            ActiveBackgroundColorHex = hex;
        }
        else
        {
            BackgroundColorHex = hex;
        }
        OnPropertyChanged(nameof(CurrentEditBgColor));
    }

    public void SelectTextColor(string hex)
    {
        if (SelectedEditState == 1)
        {
            ActiveTextColorHex = hex;
        }
        else
        {
            TextColorHex = hex;
        }
        OnPropertyChanged(nameof(CurrentEditTitleColor));
    }

    public void SelectSubtitleColor(string hex)
    {
        if (SelectedEditState == 1)
        {
            ActiveSubtitleColorHex = hex;
        }
        else
        {
            SubtitleColorHex = hex;
        }
        OnPropertyChanged(nameof(CurrentEditSubtitleColor));
    }
}

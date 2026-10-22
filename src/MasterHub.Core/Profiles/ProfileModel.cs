using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Models;

namespace MasterHub.Core.Profiles;

public class KeyAssignmentModel
{
    public int Index { get; set; }
    public DeckActionType Action { get; set; } = DeckActionType.None;
    public KeyPressMode PressMode { get; set; } = KeyPressMode.OnPress;
    public string CustomTitle { get; set; } = "";
    public string CustomSubtitle { get; set; } = "";
    public string BackgroundColorHex { get; set; } = "";
    public string TextColorHex { get; set; } = "";
    public string SubtitleColorHex { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string ActiveImagePath { get; set; } = ""; // Ścieżka do grafiki dla stanu aktywnego / toggle
    public string ActiveCustomTitle { get; set; } = "";
    public string ActiveCustomSubtitle { get; set; } = "";
    public string ActiveBackgroundColorHex { get; set; } = "";
    public string ActiveTextColorHex { get; set; } = "";
    public string ActiveSubtitleColorHex { get; set; } = "";
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 0; // 0 = Auto
    public bool IsBold { get; set; } = true;
    public bool IsItalic { get; set; } = false;
    public bool IsStrikeThrough { get; set; } = false;
    public string ActionParameter { get; set; } = ""; // Np. nazwa profilu Razer lub urządzenia
    public KeyStateMode StateMode { get; set; } = KeyStateMode.Static;

    // --- ETAP 3.5: KADROWANIE I DOPASOWANIE MEDIÓW (ZOOM, PAN, FIT) ---
    public string ImageFitMode { get; set; } = "Cover";
    public int ImageScale { get; set; } = 100;
    public int ImageOffsetX { get; set; } = 0;
    public int ImageOffsetY { get; set; } = 0;
    public string ActiveImageFitMode { get; set; } = "Cover";
    public int ActiveImageScale { get; set; } = 100;
    public int ActiveImageOffsetX { get; set; } = 0;
    public int ActiveImageOffsetY { get; set; } = 0;

    // --- ETAP 1.2: SPECYFIKACJA SYSTEMU KONFIGURACJI PER KLAWISZ ---
    public string? ApplicationPath { get; set; }
    public string? ApplicationArguments { get; set; }
    public string? Url { get; set; }
    public List<string>? HotkeyKeys { get; set; }
    public List<string>? HotkeyAltKeys { get; set; }
    public string? TextContent { get; set; }
    public string? CustomIconPath { get; set; }
    public string? LabelText { get; set; }
    public string? LabelColor { get; set; }
    public bool ShowLabel { get; set; } = true;
    public List<MultiActionStepModel>? MultiActions { get; set; }

    // --- ETAP 4.4: OBS STUDIO — PRZEŁĄCZANIE SCEN ---
    public string? ObsSceneName { get; set; }

    // --- ETAP 8.2: AKCJE WARUNKOWE (CONDITIONAL ACTIONS) ---
    public ConditionalActionModel? ConditionalAction { get; set; }

    // --- INTEGRACJA WTYCZEK ELGATO STREAM DECK ---
    public string? StreamDeckPluginUuid { get; set; }
    public string? StreamDeckActionUuid { get; set; }
    public string? StreamDeckSettingsJson { get; set; }
}

public class SliderAssignmentModel
{
    public int Index { get; set; }
    public AudioTargetType TargetType { get; set; } = AudioTargetType.None;
    public string TargetProcessName { get; set; } = "";
    public string DisplayTarget { get; set; } = "⚪ Brak (Nieprzypisany)";
    public string ColorHex { get; set; } = "#000000";
    public int Brightness { get; set; } = 0;
}

public class RollerAssignmentModel
{
    public int Index { get; set; }
    public RollerActionType Action { get; set; } = RollerActionType.None;
    public string ColorHex { get; set; } = "#000000";
    public int Brightness { get; set; } = 0;
}

public enum KnobLedMode
{
    Disable = 0,
    Static = 1,
    Breathing = 2,
    Strobe = 3,
    Spring = 4,
    Cycling = 5,
    Random = 6,
    Wave = 7,
    Water = 8,
    Rainbow = 9
}

public class KnobAssignmentModel
{
    public int Index { get; set; } // 0..2
    public RollerActionType RotateAction { get; set; } = RollerActionType.None;
    public DeckActionType PressAction { get; set; } = DeckActionType.None;
    public string PressActionParameter { get; set; } = "";
    public KnobLedMode LedMode { get; set; } = KnobLedMode.Static;
    public string ColorHex { get; set; } = "#00E5A3";
    public int Brightness { get; set; } = 100; // 0..100
    public int Speed { get; set; } = 50; // 0..100
}

public class DialAssignmentModel
{
    public RollerActionType RotateAction { get; set; } = RollerActionType.None;
    public DeckActionType PressAction { get; set; } = DeckActionType.None;
    public string PressActionParameter { get; set; } = "";
    public byte Brightness { get; set; } = 100; // 0..100
    public string ColorHex { get; set; } = "#00E5A3";
    public string? ImagePath { get; set; }
    public string DisplayWidget { get; set; } = "Image"; // "Image", "Clock", "CpuDial", "Volume"
}

public class SmartProfileRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProfileId { get; set; } = "";
    public string ProcessName { get; set; } = ""; // np. "obs64.exe", "spotify.exe"
    public string? WindowTitle { get; set; } // opcjonalny fragment tytułu okna
    public bool IsEnabled { get; set; } = true;

    public bool Matches(string processName, string windowTitle)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(ProcessName)) return false;

        string target = ProcessName.Trim();
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            target = target[..^4];

        string incoming = (processName ?? "").Trim();
        if (incoming.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            incoming = incoming[..^4];

        if (!string.Equals(target, incoming, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(WindowTitle))
        {
            if (string.IsNullOrWhiteSpace(windowTitle) ||
                !windowTitle.Contains(WindowTitle, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

public class KeyFolderModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Nowy Folder";
    public string? ParentFolderId { get; set; }
    public List<KeyAssignmentModel> Keys { get; set; } = new();

    public static KeyFolderModel Create(string name, string? parentFolderId = null)
    {
        var folder = new KeyFolderModel
        {
            Name = name,
            ParentFolderId = parentFolderId
        };

        // Automatyczny przycisk powrotu na klawiszu 0 (standard Stream Deck)
        folder.Keys.Add(new KeyAssignmentModel
        {
            Index = 0,
            Action = DeckActionType.GoBack,
            CustomTitle = "POWRÓT",
            CustomSubtitle = "Wróć",
            BackgroundColorHex = "#1E293B"
        });

        for (int i = 1; i < 15; i++)
        {
            folder.Keys.Add(new KeyAssignmentModel
            {
                Index = i,
                Action = DeckActionType.None,
                BackgroundColorHex = "#000000"
            });
        }

        return folder;
    }
}

public enum DeckWallpaperMode
{
    PerKey = 0,
    PanoramicDeck = 1
}

public class KeyPageModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Strona 1";
    public int PageIndex { get; set; } = 0;
    public List<KeyAssignmentModel> Keys { get; set; } = new();

    public DeckWallpaperMode WallpaperMode { get; set; } = DeckWallpaperMode.PerKey;
    public string? PanoramicWallpaperPath { get; set; }
    public string PanoramicWallpaperFitMode { get; set; } = "Cover";

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static KeyPageModel Create(int pageIndex, string? name = null)
    {
        var page = new KeyPageModel
        {
            PageIndex = pageIndex,
            Name = name ?? $"Strona {pageIndex + 1}"
        };

        for (int i = 0; i < 15; i++)
        {
            page.Keys.Add(new KeyAssignmentModel
            {
                Index = i,
                Action = DeckActionType.None,
                BackgroundColorHex = "#000000"
            });
        }

        return page;
    }
}

public class ProfileModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Domyślny";
    public bool IsDefault { get; set; } = false;
    public string LedColorHex { get; set; } = "#000000";
    public int BaseBrightness { get; set; } = 0;
    public int KeypadBrightness { get; set; } = 100;
    public int MasterBrightness { get; set; } = 100;

    // --- ORIENTACJA I ROTACJA MODUŁÓW ---
    public DeckRotationMode RotationMode { get; set; } = DeckRotationMode.Auto;
    public int ManualRotationAngle { get; set; } = 0; // 0, 90, 180, 270

    // --- PEŁNOPANELOWY OBRAZ (CMD-03) ---
    public DeckWallpaperMode WallpaperMode { get; set; } = DeckWallpaperMode.PerKey;
    public string? PanoramicWallpaperPath { get; set; }
    public string PanoramicWallpaperFitMode { get; set; } = "Cover";

    public List<KeyAssignmentModel> Keys { get; set; } = new();
    public List<SliderAssignmentModel> Sliders { get; set; } = new();
    public List<RollerAssignmentModel> Rollers { get; set; } = new();
    public List<KnobAssignmentModel> Knobs { get; set; } = new();
    public DialAssignmentModel Dial { get; set; } = new();

    // --- ETAP 2: STRONY, FOLDERY I SMART PROFILES ---
    public List<KeyPageModel> Pages { get; set; } = new();
    public List<KeyFolderModel> Folders { get; set; } = new();
    public List<SmartProfileRule> SmartRules { get; set; } = new();

    // --- ETAP 4: INTEGRACJA OBS STUDIO ---
    public string ObsHost { get; set; } = "localhost";
    public int ObsPort { get; set; } = 4455;
    public string? ObsPassword { get; set; }
    public bool ObsAutoConnect { get; set; } = false;

    // --- ETAP 4.6 / 4.7: INTEGRACJA TWITCH CHAT ---
    public string TwitchChannel { get; set; } = "";
    public string TwitchUsername { get; set; } = "";
    public string? TwitchOAuthToken { get; set; }
    public bool TwitchAutoConnect { get; set; } = false;

    // --- ETAP 4.9: INTEGRACJA YOUTUBE LIVE ---
    public string YouTubeChannelOrVideoId { get; set; } = "";
    public string? YouTubeApiKey { get; set; }
    public bool YouTubeAutoPoll { get; set; } = false;

    // --- ETAP 4.10: INTEGRACJA STREAMLABS DESKTOP ---
    public string StreamlabsHost { get; set; } = "127.0.0.1";
    public int StreamlabsPort { get; set; } = 59650;
    public string? StreamlabsApiToken { get; set; }
    public bool StreamlabsAutoConnect { get; set; } = false;

    // --- ETAP 5.1: INTEGRACJA DISCORD LOCAL IPC ---
    public string DiscordClientId { get; set; } = "207646673902501888";
    public string? DiscordAccessToken { get; set; }
    public bool DiscordAutoConnect { get; set; } = false;

    public static ProfileModel CreateEmpty(string name, bool isDefault = false)
    {
        var p = new ProfileModel
        {
            Name = name,
            IsDefault = isDefault,
            LedColorHex = "#000000",
            BaseBrightness = 0,
            KeypadBrightness = 100,
            MasterBrightness = 100
        };

        for (int i = 0; i < 15; i++)
        {
            p.Keys.Add(new KeyAssignmentModel { Index = i, Action = DeckActionType.None, BackgroundColorHex = "#000000" });
        }

        // Zainicjalizuj pierwszą stronę z kluczami głównymi
        p.Pages.Add(new KeyPageModel
        {
            PageIndex = 0,
            Name = "Strona 1",
            Keys = new List<KeyAssignmentModel>(p.Keys)
        });

        for (int i = 0; i < 5; i++)
        {
            p.Sliders.Add(new SliderAssignmentModel
            {
                Index = i,
                TargetType = AudioTargetType.None,
                DisplayTarget = "⚪ Brak (Nieprzypisany)",
                TargetProcessName = "",
                ColorHex = "#000000",
                Brightness = 0
            });
        }

        p.Rollers.Add(new RollerAssignmentModel { Index = 0, Action = RollerActionType.None, ColorHex = "#000000", Brightness = 0 });
        p.Rollers.Add(new RollerAssignmentModel { Index = 1, Action = RollerActionType.None, ColorHex = "#000000", Brightness = 0 });

        for (int i = 0; i < 3; i++)
        {
            p.Knobs.Add(new KnobAssignmentModel
            {
                Index = i,
                RotateAction = RollerActionType.None,
                PressAction = DeckActionType.None,
                LedMode = KnobLedMode.Static,
                ColorHex = "#00E5A3",
                Brightness = 100,
                Speed = 50
            });
        }

        p.Dial = new DialAssignmentModel
        {
            RotateAction = RollerActionType.None,
            PressAction = DeckActionType.None,
            Brightness = 100,
            ColorHex = "#00E5A3",
            DisplayWidget = "Image"
        };

        return p;
    }
}

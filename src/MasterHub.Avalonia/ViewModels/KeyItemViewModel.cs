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

public partial class KeyItemViewModel : ObservableObject
{
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, value, propertyName);
    }
    private bool _isPressed;
    private bool _isSelected;
    private string _title = "";
    private string _customTitle = "";
    private string _customSubtitle = "";
    private string _backgroundColorHex = "#000000";
    private string _textColorHex = "#FFFFFF";
    private string _subtitleColorHex = "#A1A1AA";
    private string _imagePath = "";
    private string _activeImagePath = "";
    private string _activeCustomTitle = "";
    private string _activeCustomSubtitle = "";
    private string _activeBackgroundColorHex = "";
    private string _activeTextColorHex = "";
    private string _activeSubtitleColorHex = "";
    private string _imageFitMode = "Cover";
    private int _imageScale = 100;
    private int _imageOffsetX;
    private int _imageOffsetY;
    private string _activeImageFitMode = "Cover";
    private int _activeImageScale = 100;
    private int _activeImageOffsetX;
    private int _activeImageOffsetY;
    private int _selectedEditState; // 0 = Stan 1 (Wyłączony/Domyślny), 1 = Stan 2 (Włączony/Aktywny)
    private string _fontFamily = "Segoe UI";
    private int _fontSize = 0; // 0 = Auto
    private bool _isBold = true;
    private bool _isItalic;
    private bool _isStrikeThrough;
    private DeckActionType _assignedAction = DeckActionType.None;
    private KeyPressMode _pressMode = KeyPressMode.OnPress;
    private string _actionParameter = "";
    private string _obsSceneName = "";
    private Bitmap? _cachedBitmap;
    private Bitmap? _cachedActiveBitmap;
    private bool _isActiveToggleState;
    private string _streamDeckPluginUuid = "";
    private string _streamDeckActionUuid = "";
    private string _streamDeckSettingsJson = "";

    [ObservableProperty]
    private bool _isPanoramicWallpaperActive;

    [ObservableProperty]
    private string? _panoramicWallpaperPath;

    [ObservableProperty]
    private string _panoramicWallpaperFitMode = "Cover";

    public string PanoramicFileName => string.IsNullOrEmpty(PanoramicWallpaperPath)
        ? LocalizationService.Instance.GetString("Panoramic_NoFile", "Brak tapety")
        : Path.GetFileName(PanoramicWallpaperPath);

    public Action<string, string>? ApplyPanoramicWallpaperRequested { get; set; }
    public Action? ClearPanoramicWallpaperRequested { get; set; }

    public void RequestApplyPanoramicWallpaper(string path, string fitMode = "Cover")
    {
        ApplyPanoramicWallpaperRequested?.Invoke(path, fitMode);
    }

    public void RequestClearPanoramicWallpaper()
    {
        ClearPanoramicWallpaperRequested?.Invoke();
    }

    public bool IsPanoramicFitCover => string.Equals(PanoramicWallpaperFitMode, "Cover", StringComparison.OrdinalIgnoreCase);
    public bool IsPanoramicFitContain => string.Equals(PanoramicWallpaperFitMode, "Contain", StringComparison.OrdinalIgnoreCase);
    public bool IsPanoramicFitStretch => string.Equals(PanoramicWallpaperFitMode, "Stretch", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    public void SetPanoramicFitMode(string fitMode)
    {
        PanoramicWallpaperFitMode = fitMode;
        OnPropertyChanged(nameof(IsPanoramicFitCover));
        OnPropertyChanged(nameof(IsPanoramicFitContain));
        OnPropertyChanged(nameof(IsPanoramicFitStretch));
        if (IsPanoramicWallpaperActive && !string.IsNullOrWhiteSpace(PanoramicWallpaperPath))
        {
            RequestApplyPanoramicWallpaper(PanoramicWallpaperPath, fitMode);
        }
    }

    partial void OnPanoramicWallpaperPathChanged(string? value)
    {
        OnPropertyChanged(nameof(PanoramicFileName));
    }

    partial void OnPanoramicWallpaperFitModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsPanoramicFitCover));
        OnPropertyChanged(nameof(IsPanoramicFitContain));
        OnPropertyChanged(nameof(IsPanoramicFitStretch));
        if (IsPanoramicWallpaperActive && !string.IsNullOrWhiteSpace(PanoramicWallpaperPath))
        {
            RequestApplyPanoramicWallpaper(PanoramicWallpaperPath, value);
        }
    }

    public int Index { get; set; }
    public int Number => Index + 1;
    public string KeyNumberDisplay => string.Format(LocalizationService.Instance.GetString("Key_NumberFormat", "Klawisz #{0}"), Number);
    public Action<KeyItemViewModel>? ActionChangedCallback { get; set; }

    public KeyItemViewModel()
    {
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        var vmService = App.Services?.GetService<IVoicemodService>();
        if (vmService != null)
        {
            vmService.ConnectionStateChanged += OnVoicemodConnectionChanged;
            vmService.VoicesLoaded += OnVoicemodVoicesLoaded;
            vmService.SoundsLoaded += OnVoicemodSoundsLoaded;
        }

        var haService = App.Services?.GetService<IHomeAssistantService>();
        if (haService != null)
        {
            haService.ConnectionStateChanged += OnHomeAssistantConnectionChanged;
            haService.EntitiesUpdated += OnHomeAssistantEntitiesUpdated;
            haService.EntityStateChanged += OnHomeAssistantEntityStateChanged;
        }
    }

    private void OnHomeAssistantConnectionChanged(object? sender, bool connected)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsHomeAssistantConnected));
            OnPropertyChanged(nameof(HomeAssistantConnectionStatusDisplay));
        });
    }

    private void OnHomeAssistantEntitiesUpdated(object? sender, IReadOnlyList<HomeAssistantEntityModel> entities)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AvailableHomeAssistantEntities));
            OnPropertyChanged(nameof(SelectedHomeAssistantEntity));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(DisplaySubtitle));
        });
    }

    private void OnHomeAssistantEntityStateChanged(object? sender, HomeAssistantEntityModel entity)
    {
        if (IsHomeAssistantAction && !string.IsNullOrWhiteSpace(ActionParameter) &&
            string.Equals(entity.EntityId, ActionParameter, StringComparison.OrdinalIgnoreCase))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                ActionChangedCallback?.Invoke(this);
            });
        }
    }

    private void OnVoicemodConnectionChanged(bool connected)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsVoicemodConnected));
            OnPropertyChanged(nameof(VoicemodConnectionStatusDisplay));
        });
    }

    private void OnVoicemodVoicesLoaded(IReadOnlyList<VoicemodVoiceItem> voices)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AvailableVoicemodVoices));
            OnPropertyChanged(nameof(SelectedVoicemodVoice));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(DisplaySubtitle));
        });
    }

    private void OnVoicemodSoundsLoaded(IReadOnlyList<VoicemodSoundItem> sounds)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AvailableVoicemodSounds));
            OnPropertyChanged(nameof(SelectedVoicemodSound));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(DisplaySubtitle));
        });
    }

    private void OnLanguageChanged(string lang)
    {
        OnPropertyChanged(nameof(KeyNumberDisplay));
        OnPropertyChanged(nameof(ActionDisplayName));
        OnPropertyChanged(nameof(ActionCategory));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplaySubtitle));
        OnPropertyChanged(nameof(PressModeDisplay));
        OnPropertyChanged(nameof(AvailablePressModes));
        OnPropertyChanged(nameof(SelectedPressModeOption));
        OnPropertyChanged(nameof(StateModeDisplay));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ActionParameterLabel));
        OnPropertyChanged(nameof(ActionParameterWatermark));
        OnPropertyChanged(nameof(SubtitleWatermark));
        OnPropertyChanged(nameof(PanoramicFileName));
        ActionChangedCallback?.Invoke(this);
    }

    public record PressModeOption(KeyPressMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<PressModeOption> AvailablePressModes => new PressModeOption[]
    {
        new(KeyPressMode.OnPress, LocalizationService.Instance.GetString("KeyPress_OnPress", "⬇️ Wciśnięcie (natychmiast)")),
        new(KeyPressMode.OnRelease, LocalizationService.Instance.GetString("KeyPress_OnRelease", "⬆️ Zwolnienie (puszczenie)")),
        new(KeyPressMode.OnHold, LocalizationService.Instance.GetString("KeyPress_OnHold", "⏱️ Przytrzymanie (min. 500 ms)")),
        new(KeyPressMode.OnDoublePress, LocalizationService.Instance.GetString("KeyPress_OnDoublePress", "⚡ Podwójne kliknięcie (300 ms)")),
        new(KeyPressMode.OnHoldRepeat, LocalizationService.Instance.GetString("KeyPress_OnHoldRepeat", "🔁 Powtarzanie przy trzymaniu (100 ms)"))
    };

    public PressModeOption SelectedPressModeOption
    {
        get => System.Linq.Enumerable.FirstOrDefault(AvailablePressModes, x => x.Mode == PressMode) ?? AvailablePressModes[0];
        set
        {
            if (value != null && value.Mode != PressMode)
            {
                PressMode = value.Mode;
            }
        }
    }

    public KeyPressMode PressMode
    {
        get => _pressMode;
        set
        {
            if (SetField(ref _pressMode, value))
            {
                OnPropertyChanged(nameof(PressModeDisplay));
                OnPropertyChanged(nameof(SelectedPressModeOption));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string PressModeDisplay => PressMode switch
    {
        KeyPressMode.OnPress => LocalizationService.Instance.GetString("KeyPress_OnPress", "⬇️ Wciśnięcie (natychmiast)"),
        KeyPressMode.OnRelease => LocalizationService.Instance.GetString("KeyPress_OnRelease", "⬆️ Zwolnienie (puszczenie)"),
        KeyPressMode.OnHold => LocalizationService.Instance.GetString("KeyPress_OnHold", "⏱️ Przytrzymanie (min. 500 ms)"),
        KeyPressMode.OnDoublePress => LocalizationService.Instance.GetString("KeyPress_OnDoublePress", "⚡ Podwójne kliknięcie (300 ms)"),
        KeyPressMode.OnHoldRepeat => LocalizationService.Instance.GetString("KeyPress_OnHoldRepeat", "🔁 Powtarzanie przy trzymaniu (100 ms)"),
        _ => PressMode.ToString()
    };

    private KeyStateMode _stateMode = KeyStateMode.Static;
    public KeyStateMode StateMode
    {
        get => _stateMode;
        set
        {
            if (SetField(ref _stateMode, value))
            {
                OnPropertyChanged(nameof(StateModeDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string StateModeDisplay => StateMode switch
    {
        KeyStateMode.Static => LocalizationService.Instance.GetString("KeyState_Static", "🖼️ Statyczny (Stała ikona)"),
        KeyStateMode.Toggle => LocalizationService.Instance.GetString("KeyState_Toggle", "🔁 Przełącznik (2 stany)"),
        KeyStateMode.LiveFeedback => LocalizationService.Instance.GetString("KeyState_LiveFeedback", "📡 Live Feedback (Stan na żywo)"),
        _ => StateMode.ToString()
    };

    public bool IsAutoExtractedImage => !string.IsNullOrWhiteSpace(_imagePath) &&
        _imagePath.Contains("ExtractedIcons", StringComparison.OrdinalIgnoreCase);

    public void AutoExtractIconIfApplicable()
    {
        if (string.IsNullOrWhiteSpace(_actionParameter)) return;

        if (!string.IsNullOrWhiteSpace(_imagePath) && File.Exists(_imagePath) && !IsAutoExtractedImage)
            return;

        if (AssignedAction is DeckActionType.OpenApplication or
            DeckActionType.OpenFile or
            DeckActionType.OpenWebsite or
            DeckActionType.None)
        {
            try
            {
                var extractor = App.Services?.GetService<IAppIconExtractorService>();
                string? iconPath = extractor?.ExtractAndSaveIcon(_actionParameter, 72);
                if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                {
                    ImagePath = iconPath;
                }
            }
            catch { }
        }
    }

    public string ActionParameter
    {
        get => _actionParameter;
        set
        {
            if (SetField(ref _actionParameter, value))
            {
                if (AssignedAction == DeckActionType.ObsSceneSwitch)
                {
                    _obsSceneName = value;
                }
                AutoExtractIconIfApplicable();
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string ObsSceneName
    {
        get => _obsSceneName;
        set
        {
            if (SetField(ref _obsSceneName, value))
            {
                if (AssignedAction == DeckActionType.ObsSceneSwitch)
                {
                    _actionParameter = value;
                }
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string StreamDeckPluginUuid
    {
        get => _streamDeckPluginUuid;
        set
        {
            if (SetField(ref _streamDeckPluginUuid, value))
            {
                OnPropertyChanged(nameof(ActionDisplayName));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string StreamDeckActionUuid
    {
        get => _streamDeckActionUuid;
        set
        {
            if (SetField(ref _streamDeckActionUuid, value))
            {
                OnPropertyChanged(nameof(ActionDisplayName));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string StreamDeckSettingsJson
    {
        get => _streamDeckSettingsJson;
        set
        {
            if (SetField(ref _streamDeckSettingsJson, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public record IconPreset(string Glyph, string Label);
    public record IconCategory(string CategoryName, System.Collections.Generic.IReadOnlyList<IconPreset> Icons);

    public static readonly System.Collections.Generic.IReadOnlyList<string> AvailableFontFamilies = new string[]
    {
        "Segoe UI",
        "Arial",
        "Bahnschrift",
        "Consolas",
        "Impact",
        "Trebuchet MS",
        "Verdana"
    };

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

    public bool IsPressed
    {
        get => _isPressed;
        set
        {
            if (SetField(ref _isPressed, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CurrentDisplayBitmap));
                OnPropertyChanged(nameof(HasAnyImage));
            }
        }
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string CustomTitle
    {
        get => _customTitle;
        set
        {
            if (SetField(ref _customTitle, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(HasSubtitle));
                OnPropertyChanged(nameof(HasAction));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string CustomSubtitle
    {
        get => _customSubtitle;
        set
        {
            if (SetField(ref _customSubtitle, value))
            {
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(HasSubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    private string? _liveValueText;
    public string? LiveValueText
    {
        get => _liveValueText;
        set
        {
            if (SetField(ref _liveValueText, value))
            {
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(HasSubtitle));
                OnPropertyChanged(nameof(SubtitleWatermark));
            }
        }
    }

    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set
        {
            if (SetField(ref _backgroundColorHex, value))
            {
                OnPropertyChanged(nameof(BackgroundColorBrush));
                OnPropertyChanged(nameof(BackgroundColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public IBrush BackgroundColorBrush
    {
        get
        {
            try
            {
                if (Color.TryParse(_backgroundColorHex, out var col))
                {
                    return new SolidColorBrush(col);
                }
            }
            catch { }
            return new SolidColorBrush(Color.FromRgb(0, 0, 0));
        }
    }

    public Color BackgroundColor
    {
        get => Color.TryParse(_backgroundColorHex, out var c) ? c : Color.FromRgb(0, 0, 0);
        set => BackgroundColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public string TextColorHex
    {
        get => _textColorHex;
        set
        {
            if (SetField(ref _textColorHex, value))
            {
                OnPropertyChanged(nameof(TextColorBrush));
                OnPropertyChanged(nameof(TitleColorBrush));
                OnPropertyChanged(nameof(TitleColorHex));
                OnPropertyChanged(nameof(TitleColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public IBrush TextColorBrush
    {
        get
        {
            try
            {
                if (Color.TryParse(_textColorHex, out var col))
                {
                    return new SolidColorBrush(col);
                }
            }
            catch { }
            return new SolidColorBrush(Colors.White);
        }
    }

    public string TitleColorHex
    {
        get => TextColorHex;
        set => TextColorHex = value;
    }

    public IBrush TitleColorBrush => TextColorBrush;

    public Color TitleColor
    {
        get => Color.TryParse(_textColorHex, out var c) ? c : Colors.White;
        set => TextColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public string SubtitleColorHex
    {
        get => _subtitleColorHex;
        set
        {
            if (SetField(ref _subtitleColorHex, value))
            {
                OnPropertyChanged(nameof(SubtitleColorBrush));
                OnPropertyChanged(nameof(SubtitleColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public IBrush SubtitleColorBrush
    {
        get
        {
            try
            {
                if (Color.TryParse(_subtitleColorHex, out var col))
                {
                    return new SolidColorBrush(col);
                }
            }
            catch { }
            return new SolidColorBrush(Color.FromRgb(161, 161, 170));
        }
    }

    public Color SubtitleColor
    {
        get => Color.TryParse(_subtitleColorHex, out var c) ? c : Color.FromRgb(161, 161, 170);
        set => SubtitleColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public static Bitmap? LoadMediaBitmap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            if (MediaValidationService.IsAnimatedOrVideo(path))
            {
                byte[]? firstFrameJpeg = MediaFrameExtractor.GetFirstFrameJpeg(path);
                if (firstFrameJpeg != null && firstFrameJpeg.Length > 0)
                {
                    using var ms = new MemoryStream(firstFrameJpeg);
                    return new Bitmap(ms);
                }
            }
            else if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                byte[]? pngBytes = SvgRasterizer.RasterizeSvgToPngBytes(path, 144, 144);
                if (pngBytes != null && pngBytes.Length > 0)
                {
                    using var ms = new MemoryStream(pngBytes);
                    return new Bitmap(ms);
                }
            }

            try
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 144);
            }
            catch
            {
                return new Bitmap(path);
            }
        }
        catch
        {
            return null;
        }
    }

    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (SetField(ref _imagePath, value))
            {
                SetRenderedJpeg(null);
                _cachedBitmap?.Dispose();
                _cachedBitmap = LoadMediaBitmap(_imagePath);

                OnPropertyChanged(nameof(HasCustomImage));
                OnPropertyChanged(nameof(ImageFileName));
                OnPropertyChanged(nameof(CustomImageBitmap));
                OnPropertyChanged(nameof(CurrentDisplayBitmap));
                OnPropertyChanged(nameof(HasAnyImage));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool HasCustomImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);
    public string ImageFileName => HasCustomImage ? Path.GetFileName(ImagePath) : "Brak grafiki";
    public Bitmap? CustomImageBitmap => _cachedBitmap;

    public string ActiveImagePath
    {
        get => _activeImagePath;
        set
        {
            if (SetField(ref _activeImagePath, value))
            {
                SetRenderedJpeg(null);
                _cachedActiveBitmap?.Dispose();
                _cachedActiveBitmap = LoadMediaBitmap(_activeImagePath);

                OnPropertyChanged(nameof(HasActiveImage));
                OnPropertyChanged(nameof(ActiveImageFileName));
                OnPropertyChanged(nameof(ActiveImageBitmap));
                OnPropertyChanged(nameof(CurrentDisplayBitmap));
                OnPropertyChanged(nameof(HasAnyImage));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool HasActiveImage => !string.IsNullOrWhiteSpace(ActiveImagePath) && File.Exists(ActiveImagePath);
    public string ActiveImageFileName => HasActiveImage ? Path.GetFileName(ActiveImagePath) : "Brak grafiki";
    public Bitmap? ActiveImageBitmap => _cachedActiveBitmap;

    private string? _mediaValidationMessage;
    public string? MediaValidationMessage
    {
        get => _mediaValidationMessage;
        set
        {
            if (SetField(ref _mediaValidationMessage, value))
            {
                OnPropertyChanged(nameof(HasMediaValidationMessage));
            }
        }
    }

    public bool HasMediaValidationMessage => !string.IsNullOrEmpty(_mediaValidationMessage);

    public bool ApplyMediaFile(string? path, bool isActiveState)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            MediaValidationMessage = null;
            if (isActiveState) ActiveImagePath = "";
            else ImagePath = "";
            return true;
        }

        var result = MediaValidationService.Instance.Validate(path);
        if (!result.IsValid)
        {
            string errKey = result.ErrorKey ?? "Media_Error_CorruptOrUnsupported";
            string template = LocalizationService.Instance.GetString(errKey, errKey);
            MediaValidationMessage = result.ErrorArgs != null && result.ErrorArgs.Length > 0
                ? string.Format(template, result.ErrorArgs)
                : template;
            return false;
        }

        MediaValidationMessage = null;
        if (isActiveState)
        {
            ActiveImagePath = path;
        }
        else
        {
            ImagePath = path;
        }
        return true;
    }

    public bool IsActiveToggleState
    {
        get => _isActiveToggleState;
        set
        {
            if (SetField(ref _isActiveToggleState, value))
            {
                OnPropertyChanged(nameof(CurrentDisplayBitmap));
                OnPropertyChanged(nameof(HasAnyImage));
                OnPropertyChanged(nameof(ToggleStateLabel));
                OnPropertyChanged(nameof(ActionIcon));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                NotifyCurrentEditProperties();
            }
        }
    }

    public string ToggleStateLabel => IsActiveToggleState ? "Stan 2 (Aktywny)" : "Stan 1 (Domyślny)";

    public string ActiveCustomTitle
    {
        get => _activeCustomTitle;
        set
        {
            if (SetField(ref _activeCustomTitle, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(CurrentEditTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string ActiveCustomSubtitle
    {
        get => _activeCustomSubtitle;
        set
        {
            if (SetField(ref _activeCustomSubtitle, value))
            {
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(CurrentEditSubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string ActiveBackgroundColorHex
    {
        get => _activeBackgroundColorHex;
        set
        {
            if (SetField(ref _activeBackgroundColorHex, value))
            {
                OnPropertyChanged(nameof(ActiveBackgroundColor));
                OnPropertyChanged(nameof(CurrentEditBgColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public Color ActiveBackgroundColor
    {
        get => Color.TryParse(_activeBackgroundColorHex, out var c) ? c : Color.FromRgb(31, 22, 51);
        set => ActiveBackgroundColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public string ActiveTextColorHex
    {
        get => _activeTextColorHex;
        set
        {
            if (SetField(ref _activeTextColorHex, value))
            {
                OnPropertyChanged(nameof(ActiveTitleColor));
                OnPropertyChanged(nameof(CurrentEditTitleColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public Color ActiveTitleColor
    {
        get => Color.TryParse(_activeTextColorHex, out var c) ? c : Color.FromRgb(0, 255, 204);
        set => ActiveTextColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public string ActiveSubtitleColorHex
    {
        get => _activeSubtitleColorHex;
        set
        {
            if (SetField(ref _activeSubtitleColorHex, value))
            {
                OnPropertyChanged(nameof(ActiveSubtitleColor));
                OnPropertyChanged(nameof(CurrentEditSubtitleColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public Color ActiveSubtitleColor
    {
        get => Color.TryParse(_activeSubtitleColorHex, out var c) ? c : Color.FromRgb(255, 255, 255);
        set => ActiveSubtitleColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public int SelectedEditState
    {
        get => _selectedEditState;
        set
        {
            if (SetField(ref _selectedEditState, value))
            {
                OnPropertyChanged(nameof(IsEditStateDefault));
                OnPropertyChanged(nameof(IsEditStateActive));
                NotifyCurrentEditProperties();
                IsActiveToggleState = (value == 1);
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsEditStateDefault => SelectedEditState == 0;
    public bool IsEditStateActive => SelectedEditState == 1;

    [RelayCommand]
    public void SetEditState(string? stateStr)
    {
        if (int.TryParse(stateStr, out int st))
        {
            SelectedEditState = st;
        }
    }

    public void NotifyCurrentEditProperties()
    {
        OnPropertyChanged(nameof(CurrentEditTitle));
        OnPropertyChanged(nameof(CurrentEditSubtitle));
        OnPropertyChanged(nameof(CurrentEditBgColor));
        OnPropertyChanged(nameof(CurrentEditTitleColor));
        OnPropertyChanged(nameof(CurrentEditSubtitleColor));
        OnPropertyChanged(nameof(CurrentEditImageFileName));
        OnPropertyChanged(nameof(HasCurrentEditImage));
        OnPropertyChanged(nameof(CurrentEditImageFitMode));
        OnPropertyChanged(nameof(IsFitCover));
        OnPropertyChanged(nameof(IsFitContain));
        OnPropertyChanged(nameof(IsFitStretch));
        OnPropertyChanged(nameof(CurrentEditImageScale));
        OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
        OnPropertyChanged(nameof(CurrentEditImageOffsetX));
        OnPropertyChanged(nameof(CurrentEditImageOffsetY));
        OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
        OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
    }

    public string CurrentEditTitle
    {
        get => SelectedEditState == 1 ? ActiveCustomTitle : CustomTitle;
        set
        {
            if (SelectedEditState == 1)
                ActiveCustomTitle = value;
            else
                CustomTitle = value;
            OnPropertyChanged(nameof(CurrentEditTitle));
        }
    }

    public string CurrentEditSubtitle
    {
        get => SelectedEditState == 1 ? ActiveCustomSubtitle : CustomSubtitle;
        set
        {
            if (SelectedEditState == 1)
                ActiveCustomSubtitle = value;
            else
                CustomSubtitle = value;
            OnPropertyChanged(nameof(CurrentEditSubtitle));
        }
    }

    public Color CurrentEditBgColor
    {
        get => SelectedEditState == 1 ? ActiveBackgroundColor : BackgroundColor;
        set
        {
            if (SelectedEditState == 1)
                ActiveBackgroundColor = value;
            else
                BackgroundColor = value;
            OnPropertyChanged(nameof(CurrentEditBgColor));
        }
    }

    public Color CurrentEditTitleColor
    {
        get => SelectedEditState == 1 ? ActiveTitleColor : TitleColor;
        set
        {
            if (SelectedEditState == 1)
                ActiveTitleColor = value;
            else
                TitleColor = value;
            OnPropertyChanged(nameof(CurrentEditTitleColor));
        }
    }

    public Color CurrentEditSubtitleColor
    {
        get => SelectedEditState == 1 ? ActiveSubtitleColor : SubtitleColor;
        set
        {
            if (SelectedEditState == 1)
                ActiveSubtitleColor = value;
            else
                SubtitleColor = value;
            OnPropertyChanged(nameof(CurrentEditSubtitleColor));
        }
    }

    public string CurrentEditImageFileName => SelectedEditState == 1 ? ActiveImageFileName : ImageFileName;
    public bool HasCurrentEditImage => SelectedEditState == 1 ? HasActiveImage : HasCustomImage;

    public string ImageFitMode
    {
        get => _imageFitMode;
        set
        {
            if (SetField(ref _imageFitMode, value))
            {
                OnPropertyChanged(nameof(CurrentEditImageFitMode));
                OnPropertyChanged(nameof(IsFitCover));
                OnPropertyChanged(nameof(IsFitContain));
                OnPropertyChanged(nameof(IsFitStretch));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ImageScale
    {
        get => _imageScale;
        set
        {
            int clamped = Math.Clamp(value, 20, 400);
            if (SetField(ref _imageScale, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageScale));
                OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ImageOffsetX
    {
        get => _imageOffsetX;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SetField(ref _imageOffsetX, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageOffsetX));
                OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ImageOffsetY
    {
        get => _imageOffsetY;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SetField(ref _imageOffsetY, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageOffsetY));
                OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string ActiveImageFitMode
    {
        get => _activeImageFitMode;
        set
        {
            if (SetField(ref _activeImageFitMode, value))
            {
                OnPropertyChanged(nameof(CurrentEditImageFitMode));
                OnPropertyChanged(nameof(IsFitCover));
                OnPropertyChanged(nameof(IsFitContain));
                OnPropertyChanged(nameof(IsFitStretch));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ActiveImageScale
    {
        get => _activeImageScale;
        set
        {
            int clamped = Math.Clamp(value, 20, 400);
            if (SetField(ref _activeImageScale, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageScale));
                OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ActiveImageOffsetX
    {
        get => _activeImageOffsetX;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SetField(ref _activeImageOffsetX, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageOffsetX));
                OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ActiveImageOffsetY
    {
        get => _activeImageOffsetY;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SetField(ref _activeImageOffsetY, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageOffsetY));
                OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string CurrentEditImageFitMode
    {
        get => SelectedEditState == 1 ? ActiveImageFitMode : ImageFitMode;
        set
        {
            if (SelectedEditState == 1)
                ActiveImageFitMode = value;
            else
                ImageFitMode = value;

            OnPropertyChanged(nameof(CurrentEditImageFitMode));
            OnPropertyChanged(nameof(IsFitCover));
            OnPropertyChanged(nameof(IsFitContain));
            OnPropertyChanged(nameof(IsFitStretch));
        }
    }

    public bool IsFitCover => string.Equals(CurrentEditImageFitMode, "Cover", StringComparison.OrdinalIgnoreCase);
    public bool IsFitContain => string.Equals(CurrentEditImageFitMode, "Contain", StringComparison.OrdinalIgnoreCase);
    public bool IsFitStretch => string.Equals(CurrentEditImageFitMode, "Stretch", StringComparison.OrdinalIgnoreCase);

    public int CurrentEditImageScale
    {
        get => SelectedEditState == 1 ? ActiveImageScale : ImageScale;
        set
        {
            int clamped = Math.Clamp(value, 20, 400);
            if (SelectedEditState == 1)
            {
                if (ActiveImageScale != clamped)
                {
                    ActiveImageScale = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageScale));
                    OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
                }
            }
            else
            {
                if (ImageScale != clamped)
                {
                    ImageScale = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageScale));
                    OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
                }
            }
        }
    }

    public string CurrentEditImageScaleDisplay => $"{CurrentEditImageScale}%";

    public int CurrentEditImageOffsetX
    {
        get => SelectedEditState == 1 ? ActiveImageOffsetX : ImageOffsetX;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SelectedEditState == 1)
            {
                if (ActiveImageOffsetX != clamped)
                {
                    ActiveImageOffsetX = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageOffsetX));
                    OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
                }
            }
            else
            {
                if (ImageOffsetX != clamped)
                {
                    ImageOffsetX = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageOffsetX));
                    OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
                }
            }
        }
    }

    public string CurrentEditImageOffsetXDisplay => CurrentEditImageOffsetX > 0 ? $"+{CurrentEditImageOffsetX}%" : $"{CurrentEditImageOffsetX}%";

    public int CurrentEditImageOffsetY
    {
        get => SelectedEditState == 1 ? ActiveImageOffsetY : ImageOffsetY;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SelectedEditState == 1)
            {
                if (ActiveImageOffsetY != clamped)
                {
                    ActiveImageOffsetY = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageOffsetY));
                    OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
                }
            }
            else
            {
                if (ImageOffsetY != clamped)
                {
                    ImageOffsetY = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageOffsetY));
                    OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
                }
            }
        }
    }

    public string CurrentEditImageOffsetYDisplay => CurrentEditImageOffsetY > 0 ? $"+{CurrentEditImageOffsetY}%" : $"{CurrentEditImageOffsetY}%";

    [RelayCommand]
    public void SetFitMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;
        CurrentEditImageFitMode = mode;
    }

    [RelayCommand]
    public void ZoomIn()
    {
        CurrentEditImageScale = Math.Min(400, CurrentEditImageScale + 10);
    }

    [RelayCommand]
    public void ZoomOut()
    {
        CurrentEditImageScale = Math.Max(20, CurrentEditImageScale - 10);
    }

    [RelayCommand]
    public void Pan(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)) return;
        switch (direction.ToLowerInvariant())
        {
            case "up":
                CurrentEditImageOffsetY = Math.Min(100, CurrentEditImageOffsetY + 5);
                break;
            case "down":
                CurrentEditImageOffsetY = Math.Max(-100, CurrentEditImageOffsetY - 5);
                break;
            case "left":
                CurrentEditImageOffsetX = Math.Max(-100, CurrentEditImageOffsetX - 5);
                break;
            case "right":
                CurrentEditImageOffsetX = Math.Min(100, CurrentEditImageOffsetX + 5);
                break;
        }
    }

    [RelayCommand]
    public void ResetImageTransform()
    {
        if (SelectedEditState == 1)
        {
            ActiveImageFitMode = "Cover";
            ActiveImageScale = 100;
            ActiveImageOffsetX = 0;
            ActiveImageOffsetY = 0;
        }
        else
        {
            ImageFitMode = "Cover";
            ImageScale = 100;
            ImageOffsetX = 0;
            ImageOffsetY = 0;
        }
        NotifyCurrentEditProperties();
    }

    private Bitmap? _renderedBitmap;
    private byte[]? _renderedJpegBytes;
    private byte[]? _pluginDynamicImageBytes;

    public byte[]? RenderedJpegBytes => _renderedJpegBytes;
    public byte[]? PluginDynamicImageBytes => _pluginDynamicImageBytes;
    public bool IsStreamDeckAlert { get; set; }
    public bool IsStreamDeckOk { get; set; }

    public void SetPluginDynamicImage(byte[]? bytes)
    {
        _pluginDynamicImageBytes = bytes;
    }

    public void SetRenderedJpeg(byte[]? jpeg)
    {
        _renderedJpegBytes = jpeg;
        _renderedBitmap?.Dispose();
        _renderedBitmap = null;
        if (jpeg != null && jpeg.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(jpeg);
                _renderedBitmap = new Bitmap(ms);
            }
            catch
            {
                _renderedBitmap = null;
            }
        }
        OnPropertyChanged(nameof(CurrentDisplayBitmap));
        OnPropertyChanged(nameof(CachedBitmap));
        OnPropertyChanged(nameof(HasAnyImage));
    }

    public Bitmap? CurrentDisplayBitmap
    {
        get
        {
            if (_renderedBitmap != null)
            {
                return _renderedBitmap;
            }
            if ((IsActiveToggleState || IsPressed) && HasActiveImage)
            {
                return ActiveImageBitmap;
            }
            if (HasCustomImage)
            {
                return CustomImageBitmap;
            }
            return null;
        }
    }

    public Bitmap? CachedBitmap => CurrentDisplayBitmap;

    public bool HasAnyImage => CurrentDisplayBitmap != null;

    private FontFamily _cachedFontFamily = new("Segoe UI");

    public string FontFamily
    {
        get => _fontFamily;
        set
        {
            if (SetField(ref _fontFamily, value))
            {
                _cachedFontFamily = new FontFamily(!string.IsNullOrWhiteSpace(_fontFamily) ? _fontFamily : "Segoe UI");
                OnPropertyChanged(nameof(SelectedFontFamily));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public FontFamily SelectedFontFamily => _cachedFontFamily;

    public int FontSize
    {
        get => _fontSize;
        set
        {
            if (SetField(ref _fontSize, value))
            {
                OnPropertyChanged(nameof(FontSizeLabel));
                OnPropertyChanged(nameof(DisplayFontSize));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string FontSizeLabel => FontSize > 0 ? $"{FontSize} pt" : "Auto";

    public bool IsBold
    {
        get => _isBold;
        set
        {
            if (SetField(ref _isBold, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsItalic
    {
        get => _isItalic;
        set
        {
            if (SetField(ref _isItalic, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsStrikeThrough
    {
        get => _isStrikeThrough;
        set
        {
            if (SetField(ref _isStrikeThrough, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public double DisplayFontSize
    {
        get
        {
            if (_fontSize > 0) return _fontSize;
            string text = DisplayTitle;
            if (text.Contains('\n'))
            {
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return lines.Length >= 3 ? 9.5 : 11.5;
            }
            return text.Length <= 3 ? 16.0 : (text.Length <= 6 ? 12.0 : 10.0);
        }
    }

    public DeckActionType AssignedAction
    {
        get => _assignedAction;
        set
        {
            if (SetField(ref _assignedAction, value))
            {
                if (value == DeckActionType.None)
                {
                    _streamDeckPluginUuid = "";
                    _streamDeckActionUuid = "";
                    _streamDeckSettingsJson = "";
                    ResetStyling();
                }
                _liveValueText = null;
                ActionParameter = GetDefaultActionParameter(value);
                OnPropertyChanged(nameof(IsMonitoringAction));
                OnPropertyChanged(nameof(SubtitleWatermark));
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(ActionCategory));
                OnPropertyChanged(nameof(ActionIcon));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(HasAction));
                OnPropertyChanged(nameof(HasSubtitle));
                OnPropertyChanged(nameof(RequiresActionParameter));
                OnPropertyChanged(nameof(IsObsSceneAction));
                OnPropertyChanged(nameof(IsNotObsSceneAction));
                OnPropertyChanged(nameof(IsFileBrowseSupported));
                OnPropertyChanged(nameof(IsGpuMonitorAction));
                OnPropertyChanged(nameof(IsGpuAction));
                OnPropertyChanged(nameof(IsRazerDpiAction));
                OnPropertyChanged(nameof(IsRazerPollingAction));
                OnPropertyChanged(nameof(IsRazerProfileAction));
                OnPropertyChanged(nameof(IsNetworkMonitorAction));
                OnPropertyChanged(nameof(IsDiskAction));
                OnPropertyChanged(nameof(AvailableDrives));
                OnPropertyChanged(nameof(SelectedDrive));
                OnPropertyChanged(nameof(IsDiscordJoinAction));
                OnPropertyChanged(nameof(SelectedNetworkUnit));
                OnPropertyChanged(nameof(IsGenericParameterAction));
                OnPropertyChanged(nameof(IsGoToProfileAction));
                OnPropertyChanged(nameof(IsGoToPageAction));
                OnPropertyChanged(nameof(IsOpenFolderAction));
                OnPropertyChanged(nameof(IsNavigationAction));
                OnPropertyChanged(nameof(IsVoicemodVoiceAction));
                OnPropertyChanged(nameof(IsVoicemodSoundAction));
                OnPropertyChanged(nameof(IsVoicemodAnyAction));
                OnPropertyChanged(nameof(IsVoicemodConnected));
                OnPropertyChanged(nameof(VoicemodConnectionStatusDisplay));
                OnPropertyChanged(nameof(AvailableVoicemodVoices));
                OnPropertyChanged(nameof(SelectedVoicemodVoice));
                OnPropertyChanged(nameof(AvailableVoicemodSounds));
                OnPropertyChanged(nameof(SelectedVoicemodSound));
                OnPropertyChanged(nameof(AvailableProfiles));
                OnPropertyChanged(nameof(SelectedProfile));
                OnPropertyChanged(nameof(AvailablePages));
                OnPropertyChanged(nameof(SelectedPage));
                OnPropertyChanged(nameof(AvailableFolders));
                OnPropertyChanged(nameof(SelectedFolder));
                OnPropertyChanged(nameof(IsHomeAssistantAction));
                OnPropertyChanged(nameof(IsHomeAssistantConnected));
                OnPropertyChanged(nameof(HomeAssistantConnectionStatusDisplay));
                OnPropertyChanged(nameof(AvailableHomeAssistantEntities));
                OnPropertyChanged(nameof(SelectedHomeAssistantEntity));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public static string GetDefaultActionParameter(DeckActionType action) => action switch
    {
        DeckActionType.DisplayNetworkSpeed => "MB/s",
        DeckActionType.DisplayDiskUsage => "C:\\",
        DeckActionType.DisplayGpuUsage or DeckActionType.DisplayGpuTemp => "",
        DeckActionType.RazerDpiCycle or DeckActionType.RazerDpiUp or DeckActionType.RazerDpiDown or
        DeckActionType.RazerDpi400 or DeckActionType.RazerDpi800 or DeckActionType.RazerDpi1600 or
        DeckActionType.RazerDpi3200 or DeckActionType.RazerDpi6400 => "1600",
        DeckActionType.RazerPollingRateCycle or DeckActionType.RazerPollingRateUp or DeckActionType.RazerPollingRateDown or
        DeckActionType.RazerPollingRate125 or DeckActionType.RazerPollingRate500 or DeckActionType.RazerPollingRate1000 or
        DeckActionType.RazerPollingRate2000 or DeckActionType.RazerPollingRate4000 or DeckActionType.RazerPollingRate8000 => "1000 Hz",
        DeckActionType.RazerProfile1 or DeckActionType.RazerProfile2 or DeckActionType.RazerProfile3 or
        DeckActionType.RazerProfile4 or DeckActionType.RazerProfile5 or DeckActionType.RazerProfileNext or DeckActionType.RazerProfilePrev => "Profil #1",
        DeckActionType.WootingProfile1 or DeckActionType.WootingProfile2 or DeckActionType.WootingProfile3 or
        DeckActionType.WootingProfileCycle or DeckActionType.WootingProfileNext or DeckActionType.WootingProfilePrev => "1",
        DeckActionType.Delay => "1000",
        DeckActionType.GoToPage => "",
        DeckActionType.GoToProfile => "",
        DeckActionType.OpenFolder => "",
        DeckActionType.NextPage => "",
        DeckActionType.PreviousPage => "",
        DeckActionType.VoicemodVoiceSelect => "nofx",
        DeckActionType.VoicemodPlaySound => "vineboom",
        DeckActionType.HomeAssistantToggle => "light.living_room",
        DeckActionType.HomeAssistantTurnOn => "light.living_room",
        DeckActionType.HomeAssistantTurnOff => "light.living_room",
        DeckActionType.HomeAssistantCallService => "light.toggle",
        _ => ""
    };

    public bool IsMonitoringAction =>
        AssignedAction is DeckActionType.DisplayCpuUsage or
                          DeckActionType.DisplayRamUsage or
                          DeckActionType.DisplayGpuUsage or
                          DeckActionType.DisplayCpuTemp or
                          DeckActionType.DisplayGpuTemp or
                          DeckActionType.DisplayNetworkSpeed or
                          DeckActionType.DisplayDiskUsage or
                          DeckActionType.DisplayClock or
                          DeckActionType.DisplayDate or
                          DeckActionType.DisplayFps or
                          DeckActionType.NowPlaying or
                          DeckActionType.YouTubeLiveViewers;

    public string SubtitleWatermark =>
        IsMonitoringAction && !string.IsNullOrWhiteSpace(_liveValueText)
            ? $"{LocalizationService.Instance.GetString("Key_LiveValue", "Wartość na żywo")}: {_liveValueText}"
            : LocalizationService.Instance.GetString("Key_SubtitlePrompt", "Wpisz podtytuł...");

    public string GetEffectiveSubtitle()
    {
        if (IsMonitoringAction)
        {
            if (!string.IsNullOrWhiteSpace(_liveValueText))
            {
                return _liveValueText;
            }
            if (!string.IsNullOrWhiteSpace(CustomSubtitle))
            {
                return CustomSubtitle;
            }
            return "";
        }

        if (IsActiveToggleState && !string.IsNullOrWhiteSpace(ActiveCustomSubtitle))
        {
            return ActiveCustomSubtitle;
        }

        return CustomSubtitle;
    }

    public bool IsGpuMonitorAction =>
        AssignedAction == DeckActionType.DisplayGpuUsage || AssignedAction == DeckActionType.DisplayGpuTemp;

    public bool IsGpuAction =>
        AssignedAction is DeckActionType.DisplayGpuUsage or DeckActionType.DisplayGpuTemp;

    public bool IsRazerDpiAction =>
        AssignedAction is DeckActionType.RazerDpiCycle or DeckActionType.RazerDpiUp or DeckActionType.RazerDpiDown or
        DeckActionType.RazerDpi400 or DeckActionType.RazerDpi800 or DeckActionType.RazerDpi1600 or DeckActionType.RazerDpi3200 or DeckActionType.RazerDpi6400;

    public bool IsRazerPollingAction =>
        AssignedAction is DeckActionType.RazerPollingRateCycle or DeckActionType.RazerPollingRateUp or DeckActionType.RazerPollingRateDown or
        DeckActionType.RazerPollingRate125 or DeckActionType.RazerPollingRate500 or DeckActionType.RazerPollingRate1000 or
        DeckActionType.RazerPollingRate2000 or DeckActionType.RazerPollingRate4000 or DeckActionType.RazerPollingRate8000;

    public bool IsRazerProfileAction =>
        AssignedAction is DeckActionType.RazerProfile1 or DeckActionType.RazerProfile2 or DeckActionType.RazerProfile3 or
        DeckActionType.RazerProfile4 or DeckActionType.RazerProfile5 or DeckActionType.RazerProfileNext or DeckActionType.RazerProfilePrev;

    public bool IsNetworkMonitorAction => AssignedAction == DeckActionType.DisplayNetworkSpeed;
    public bool IsDiscordJoinAction => AssignedAction == DeckActionType.DiscordJoinVoiceChannel;

    public static readonly IReadOnlyList<string> NetworkUnitOptions = new[]
    {
        "MB/s (Bajtowa: KB/s, MB/s, GB/s)",
        "Mb/s (Bitowa: Kb/s, Mb/s, Gb/s)"
    };

    public string SelectedNetworkUnit
    {
        get => ActionParameter?.Contains("b/s", StringComparison.Ordinal) == true || ActionParameter?.Contains("bit", StringComparison.OrdinalIgnoreCase) == true
            ? NetworkUnitOptions[1]
            : NetworkUnitOptions[0];
        set
        {
            ActionParameter = (value == NetworkUnitOptions[1]) ? "Mb/s" : "MB/s";
            OnPropertyChanged();
        }
    }

    public bool IsDiskAction => AssignedAction == DeckActionType.DisplayDiskUsage;

    public IReadOnlyList<string> AvailableDrives
    {
        get
        {
            var drives = App.Services?.GetService<ISystemMonitorService>()?.AvailableDrives;
            return drives != null && drives.Count > 0 ? drives : new[] { "C:\\" };
        }
    }

    public string SelectedDrive
    {
        get => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : (AvailableDrives.Count > 0 ? AvailableDrives[0] : "C:\\");
        set
        {
            ActionParameter = value ?? "C:\\";
            OnPropertyChanged();
        }
    }

    public bool IsGoToProfileAction => AssignedAction == DeckActionType.GoToProfile;
    public bool IsGoToPageAction => AssignedAction == DeckActionType.GoToPage;
    public bool IsOpenFolderAction => AssignedAction == DeckActionType.OpenFolder;
    public bool IsGoBackAction => AssignedAction == DeckActionType.GoBack;
    public bool IsGoHomeAction => AssignedAction == DeckActionType.GoHome;
    public bool IsNextPageAction => AssignedAction == DeckActionType.NextPage;
    public bool IsPreviousPageAction => AssignedAction == DeckActionType.PreviousPage;
    public bool IsNavigationAction => IsGoBackAction || IsGoHomeAction || IsNextPageAction || IsPreviousPageAction;

    public bool IsVoicemodVoiceAction => AssignedAction == DeckActionType.VoicemodVoiceSelect;
    public bool IsVoicemodSoundAction => AssignedAction == DeckActionType.VoicemodPlaySound;
    public bool IsVoicemodBackgroundToggleAction => AssignedAction == DeckActionType.VoicemodBackgroundToggle;
    public bool IsVoicemodAnyAction => AssignedAction is
        DeckActionType.VoicemodVoiceSelect or
        DeckActionType.VoicemodVoiceToggle or
        DeckActionType.VoicemodMuteVoice or
        DeckActionType.VoicemodHearMyVoiceToggle or
        DeckActionType.VoicemodBackgroundToggle or
        DeckActionType.VoicemodPlaySound or
        DeckActionType.VoicemodStopAllSounds;

    public bool IsVoicemodConnected => App.Services?.GetService<IVoicemodService>()?.IsConnected ?? false;

    public string VoicemodConnectionStatusDisplay => IsVoicemodConnected
        ? LocalizationService.Instance.GetString("Voicemod_ConnectedStatus", "🟢 Połączono z Voicemod")
        : LocalizationService.Instance.GetString("Voicemod_DisconnectedStatus", "🔴 Rozłączono z Voicemod");

    public IReadOnlyList<VoicemodVoiceItem> AvailableVoicemodVoices
    {
        get
        {
            var svc = App.Services?.GetService<IVoicemodService>();
            var list = svc?.AvailableVoices;
            if (list != null && list.Count > 0) return list;
            return VoicemodDefaults.StandardVoices;
        }
    }

    public VoicemodVoiceItem? SelectedVoicemodVoice
    {
        get
        {
            string target = ActionParameter;
            if (string.IsNullOrWhiteSpace(target)) return AvailableVoicemodVoices.FirstOrDefault();
            return AvailableVoicemodVoices.FirstOrDefault(v =>
                string.Equals(v.Id, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v.Name, target, StringComparison.OrdinalIgnoreCase))
                ?? AvailableVoicemodVoices.FirstOrDefault();
        }
        set
        {
            if (value != null)
            {
                _actionParameter = value.Id;
                OnPropertyChanged(nameof(ActionParameter));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public IReadOnlyList<VoicemodSoundItem> AvailableVoicemodSounds
    {
        get
        {
            var svc = App.Services?.GetService<IVoicemodService>();
            var list = svc?.AvailableSounds;
            if (list != null && list.Count > 0) return list;
            return VoicemodDefaults.StandardSounds;
        }
    }

    public VoicemodSoundItem? SelectedVoicemodSound
    {
        get
        {
            string target = ActionParameter;
            if (string.IsNullOrWhiteSpace(target)) return AvailableVoicemodSounds.FirstOrDefault();
            return AvailableVoicemodSounds.FirstOrDefault(s =>
                string.Equals(s.FileName, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, target, StringComparison.OrdinalIgnoreCase))
                ?? AvailableVoicemodSounds.FirstOrDefault();
        }
        set
        {
            if (value != null)
            {
                _actionParameter = value.FileName;
                OnPropertyChanged(nameof(ActionParameter));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    [RelayCommand]
    public void OpenVoicemodSettings()
    {
        var nav = App.Services?.GetService<MasterHub.Avalonia.Services.INavigationService>();
        nav?.NavigateTo(MasterHub.Avalonia.Services.NavigationPage.Settings);
        MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel.RequestScrollToVoicemod();
    }

    public bool IsVoicemodBackgroundEffectsEnabled => App.Services?.GetService<IVoicemodService>()?.IsBackgroundEffectsEnabled ?? true;

    public string VoicemodBackgroundEffectsStatusDisplay => IsVoicemodBackgroundEffectsEnabled
        ? LocalizationService.Instance.GetString("Voicemod_BgEffects_On", "🎧 Dźwięk tła (Background FX): WŁĄCZONY")
        : LocalizationService.Instance.GetString("Voicemod_BgEffects_Off", "🔇 Dźwięk tła (Background FX): WYŁĄCZONY (Sam głos)");

    [RelayCommand]
    public async Task ToggleVoicemodBackgroundEffectsAsync()
    {
        var svc = App.Services?.GetService<IVoicemodService>();
        if (svc != null)
        {
            await svc.ToggleBackgroundEffectsAsync();
            OnPropertyChanged(nameof(IsVoicemodBackgroundEffectsEnabled));
            OnPropertyChanged(nameof(VoicemodBackgroundEffectsStatusDisplay));
        }
    }

    public void RefreshVoicemodProperties()
    {
        OnPropertyChanged(nameof(IsVoicemodConnected));
        OnPropertyChanged(nameof(VoicemodConnectionStatusDisplay));
        OnPropertyChanged(nameof(AvailableVoicemodVoices));
        OnPropertyChanged(nameof(SelectedVoicemodVoice));
        OnPropertyChanged(nameof(AvailableVoicemodSounds));
        OnPropertyChanged(nameof(SelectedVoicemodSound));
        OnPropertyChanged(nameof(IsVoicemodBackgroundEffectsEnabled));
        OnPropertyChanged(nameof(VoicemodBackgroundEffectsStatusDisplay));
    }

    public bool IsHomeAssistantAction => AssignedAction is
        DeckActionType.HomeAssistantToggle or
        DeckActionType.HomeAssistantTurnOn or
        DeckActionType.HomeAssistantTurnOff or
        DeckActionType.HomeAssistantCallService;

    public bool IsHomeAssistantConnected => App.Services?.GetService<IHomeAssistantService>()?.IsConnected ?? false;

    public string HomeAssistantConnectionStatusDisplay => IsHomeAssistantConnected
        ? LocalizationService.Instance.GetString("HA_ConnectedStatus", "🟢 Połączono z Home Assistant")
        : LocalizationService.Instance.GetString("HA_DisconnectedStatus", "🔴 Rozłączono z Home Assistant");

    public IReadOnlyList<HomeAssistantEntityModel> AvailableHomeAssistantEntities
    {
        get
        {
            var svc = App.Services?.GetService<IHomeAssistantService>();
            var list = svc?.Entities;
            if (list != null && list.Count > 0) return list;
            return Array.Empty<HomeAssistantEntityModel>();
        }
    }

    public HomeAssistantEntityModel? SelectedHomeAssistantEntity
    {
        get
        {
            string target = ActionParameter;
            if (string.IsNullOrWhiteSpace(target)) return AvailableHomeAssistantEntities.FirstOrDefault();
            return AvailableHomeAssistantEntities.FirstOrDefault(e =>
                string.Equals(e.EntityId, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.FriendlyName, target, StringComparison.OrdinalIgnoreCase))
                ?? AvailableHomeAssistantEntities.FirstOrDefault();
        }
        set
        {
            if (value != null)
            {
                _actionParameter = value.EntityId;
                OnPropertyChanged(nameof(ActionParameter));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    [RelayCommand]
    public void OpenHomeAssistantSettings()
    {
        var nav = App.Services?.GetService<MasterHub.Avalonia.Services.INavigationService>();
        nav?.NavigateTo(MasterHub.Avalonia.Services.NavigationPage.Settings);
        MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel.RequestScrollToHomeAssistant();
    }

    public bool IsGenericParameterAction =>
        RequiresActionParameter && !IsGpuAction && !IsRazerDpiAction && !IsRazerPollingAction && !IsRazerProfileAction &&
        !IsFileBrowseSupported && !IsNetworkMonitorAction && !IsDiskAction &&
        !IsGoToProfileAction && !IsGoToPageAction && !IsOpenFolderAction &&
        !IsGoBackAction && !IsGoHomeAction && !IsNextPageAction && !IsPreviousPageAction &&
        !IsVoicemodVoiceAction && !IsVoicemodSoundAction && !IsHomeAssistantAction;

    public Action<KeyItemViewModel, DeckActionType, string?>? NavigationRequestedCallback { get; set; }
    public Action<string>? FolderDeletedCallback { get; set; }

    public IReadOnlyList<string> AvailableProfiles
    {
        get
        {
            var profileService = App.Services?.GetService<IProfileService>();
            var profiles = profileService?.GetAllProfiles();
            if (profiles != null && profiles.Count > 0)
            {
                return profiles.Select(p => p.Name).ToList();
            }
            return Array.Empty<string>();
        }
    }

    public string? SelectedProfile
    {
        get => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : null;
        set
        {
            ActionParameter = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            ActionChangedCallback?.Invoke(this);
        }
    }

    public IReadOnlyList<string> AvailablePages
    {
        get
        {
            var profileService = App.Services?.GetService<IProfileService>();
            var activeProf = profileService?.GetActiveProfile();
            if (activeProf?.Pages != null && activeProf.Pages.Count > 0)
            {
                return activeProf.Pages.Select((p, idx) => $"Strona {idx + 1}").ToList();
            }
            return new[] { "Strona 1" };
        }
    }

    public string? SelectedPage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ActionParameter))
            {
                var match = AvailablePages.FirstOrDefault(p => p.Equals(ActionParameter, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;

                if (int.TryParse(ActionParameter, out int idx) && idx >= 1 && idx <= AvailablePages.Count)
                {
                    return $"Strona {idx}";
                }
            }
            return null;
        }
        set
        {
            ActionParameter = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            ActionChangedCallback?.Invoke(this);
        }
    }

    public IReadOnlyList<string> AvailableFolders
    {
        get
        {
            var profileService = App.Services?.GetService<IProfileService>();
            var activeProf = profileService?.GetActiveProfile();
            var list = new List<string>();
            if (activeProf?.Folders != null)
            {
                foreach (var f in activeProf.Folders)
                {
                    if (!string.IsNullOrWhiteSpace(f.Name) && !list.Contains(f.Name))
                    {
                        list.Add(f.Name);
                    }
                }
            }
            return list;
        }
    }

    public string? SelectedFolder
    {
        get => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ActionParameter = value;
                OnPropertyChanged(nameof(FolderNameInput));
                EnsureFolderExistsInProfile(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string FolderNameInput
    {
        get => ActionParameter;
        set
        {
            string clean = value ?? "";
            if (ActionParameter != clean)
            {
                string oldName = ActionParameter;
                ActionParameter = clean;
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    RenameOrEnsureFolderInProfile(oldName, clean);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedFolder));
                OnPropertyChanged(nameof(AvailableFolders));
                OnPropertyChanged(nameof(DisplayTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    [RelayCommand]
    public void EnterFolder()
    {
        if (AssignedAction == DeckActionType.OpenFolder)
        {
            string target = !string.IsNullOrWhiteSpace(FolderNameInput) ? FolderNameInput.Trim() : ActionParameter.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                target = "Nowy Folder";
                FolderNameInput = target;
            }
            RenameOrEnsureFolderInProfile("", target);
            NavigationRequestedCallback?.Invoke(this, DeckActionType.OpenFolder, target);
        }
    }

    [RelayCommand]
    public void TriggerGoBack()
    {
        NavigationRequestedCallback?.Invoke(this, DeckActionType.GoBack, null);
    }

    [RelayCommand]
    public void TriggerGoHome()
    {
        NavigationRequestedCallback?.Invoke(this, DeckActionType.GoHome, null);
    }

    [RelayCommand]
    public void DeleteSelectedFolder()
    {
        string target = !string.IsNullOrWhiteSpace(FolderNameInput)
            ? FolderNameInput.Trim()
            : (!string.IsNullOrWhiteSpace(SelectedFolder) ? SelectedFolder.Trim() : ActionParameter.Trim());

        if (string.IsNullOrWhiteSpace(target)) return;

        FolderDeletedCallback?.Invoke(target);

        ActionParameter = "";
        CustomTitle = "";
        OnPropertyChanged(nameof(FolderNameInput));
        OnPropertyChanged(nameof(AvailableFolders));
        OnPropertyChanged(nameof(SelectedFolder));
        ActionChangedCallback?.Invoke(this);
    }

    private void RenameOrEnsureFolderInProfile(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        var profileService = App.Services?.GetService<IProfileService>();
        var activeProf = profileService?.GetActiveProfile();
        if (activeProf != null)
        {
            activeProf.Folders ??= new List<KeyFolderModel>();
            var existingByOld = !string.IsNullOrWhiteSpace(oldName)
                ? activeProf.Folders.FirstOrDefault(f => f.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                : null;

            if (existingByOld != null)
            {
                existingByOld.Name = newName;
            }
            else
            {
                var existingByNew = activeProf.Folders.FirstOrDefault(f => f.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
                if (existingByNew == null)
                {
                    activeProf.Folders.Add(KeyFolderModel.Create(newName));
                }
            }
            profileService?.SaveProfile(activeProf);
            OnPropertyChanged(nameof(AvailableFolders));
        }
    }

    private void EnsureFolderExistsInProfile(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return;
        var profileService = App.Services?.GetService<IProfileService>();
        var activeProf = profileService?.GetActiveProfile();
        if (activeProf != null)
        {
            activeProf.Folders ??= new List<KeyFolderModel>();
            if (!activeProf.Folders.Any(f => f.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
            {
                activeProf.Folders.Add(KeyFolderModel.Create(folderName));
                profileService?.SaveProfile(activeProf);
                OnPropertyChanged(nameof(AvailableFolders));
            }
        }
    }

    public IReadOnlyList<string> AvailableGpuNames
    {
        get
        {
            var gpus = App.Services?.GetService<ISystemMonitorService>()?.AvailableGpuNames;
            return gpus != null && gpus.Count > 0 ? gpus : new[] { "Domyślna karta graficzna GPU" };
        }
    }

    public string SelectedGpuName
    {
        get => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : (AvailableGpuNames.Count > 0 ? AvailableGpuNames[0] : "");
        set
        {
            ActionParameter = value;
            var sysMon = App.Services?.GetService<ISystemMonitorService>();
            if (sysMon != null)
            {
                for (int i = 0; i < AvailableGpuNames.Count; i++)
                {
                    if (AvailableGpuNames[i] == value)
                    {
                        sysMon.SelectedGpuIndex = i;
                        break;
                    }
                }
            }
            OnPropertyChanged();
        }
    }

    public static readonly IReadOnlyList<string> DpiDropdownOptions = new[]
    {
        "400", "800", "1200", "1600", "2000", "2400", "3200", "4000", "6400", "8000", "12000", "16000", "20000", "26000", "30000"
    };

    public static readonly IReadOnlyList<string> PollingDropdownOptions = new[]
    {
        "125 Hz", "250 Hz", "500 Hz", "1000 Hz", "2000 Hz", "4000 Hz", "8000 Hz"
    };

    public static readonly IReadOnlyList<string> ProfilePresets = new[]
    {
        "Profil #1", "Profil #2", "Profil #3", "Profil #4", "Profil #5"
    };

    public string SelectedDpi
    {
        get => ActionParameter;
        set
        {
            ActionParameter = value ?? "";
            OnPropertyChanged();
        }
    }

    public string SelectedPollingRate
    {
        get => ActionParameter;
        set
        {
            ActionParameter = value ?? "";
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    public void SelectProfile(string preset)
    {
        ActionParameter = preset;
    }

    [RelayCommand]
    public void ClearAction()
    {
        _assignedAction = DeckActionType.None;
        ActionParameter = "";
        _liveValueText = null;
        _streamDeckPluginUuid = "";
        _streamDeckActionUuid = "";
        _streamDeckSettingsJson = "";
        _pluginDynamicImageBytes = null;
        IsStreamDeckAlert = false;
        IsStreamDeckOk = false;
        ResetStyling();
        OnPropertyChanged(nameof(AssignedAction));
        OnPropertyChanged(nameof(ActionDisplayName));
        OnPropertyChanged(nameof(ActionCategory));
        OnPropertyChanged(nameof(ActionIcon));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplaySubtitle));
        OnPropertyChanged(nameof(HasAction));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(RequiresActionParameter));
        OnPropertyChanged(nameof(IsMonitoringAction));
        OnPropertyChanged(nameof(LiveValueText));
        OnPropertyChanged(nameof(IsVoicemodAnyAction));
        OnPropertyChanged(nameof(IsNavigationAction));
        OnPropertyChanged(nameof(IsGenericParameterAction));
        OnPropertyChanged(nameof(IsFileBrowseSupported));
        OnPropertyChanged(nameof(StreamDeckPluginUuid));
        OnPropertyChanged(nameof(StreamDeckActionUuid));
        OnPropertyChanged(nameof(StreamDeckSettingsJson));
        ActionChangedCallback?.Invoke(this);
    }

    public bool HasAction => AssignedAction != DeckActionType.None;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(DisplaySubtitle);

    public string DisplayTitle
    {
        get
        {
            if (IsActiveToggleState && !string.IsNullOrWhiteSpace(ActiveCustomTitle)) return ActiveCustomTitle;
            if (!string.IsNullOrWhiteSpace(CustomTitle)) return CustomTitle;
            var loc = LocalizationService.Instance;
            return AssignedAction switch
            {
                DeckActionType.None => "",
                DeckActionType.MediaPlayPause => "PLAY/PAUSE",
                DeckActionType.MediaNext => loc.GetString("Deck_Next", "NASTĘPNY"),
                DeckActionType.MediaPrevious => loc.GetString("Deck_Prev", "POPRZEDNI"),
                DeckActionType.MediaStop => "STOP",
                DeckActionType.MasterMute => loc.GetString("Deck_Mute", "WYCISZ"),
                DeckActionType.MicMute => loc.GetString("Deck_Mic", "MIKROFON"),
                DeckActionType.VolumeUp => loc.GetString("Deck_VolUp", "GŁOŚNIEJ"),
                DeckActionType.VolumeDown => loc.GetString("Deck_VolDown", "CISZEJ"),
                DeckActionType.Screenshot => loc.GetString("Deck_Screenshot", "ZRZUT"),
                DeckActionType.TaskManager => loc.GetString("Deck_TaskMgr", "MENEDŻER"),
                DeckActionType.Calculator => loc.GetString("Deck_Calc", "KALKULATOR"),
                DeckActionType.Browser => loc.GetString("Deck_Browser", "INTERNET"),
                DeckActionType.OpenApplication => !string.IsNullOrWhiteSpace(ActionParameter) ? Path.GetFileNameWithoutExtension(ActionParameter).ToUpperInvariant() : "LAUNCH",
                DeckActionType.OpenWebsite => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter.Replace("https://", "").Replace("http://", "").Split('/')[0].ToUpperInvariant() : "URL",
                DeckActionType.OpenFile => !string.IsNullOrWhiteSpace(ActionParameter) ? Path.GetFileName(ActionParameter).ToUpperInvariant() : "FILE",
                DeckActionType.CloseApplication => !string.IsNullOrWhiteSpace(ActionParameter) ? $"X {Path.GetFileNameWithoutExtension(ActionParameter).ToUpperInvariant()}" : "CLOSE",
                DeckActionType.TextPaste => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter) : "PASTE",
                DeckActionType.Hotkey => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : "HOTKEY",
                DeckActionType.HotkeySwitch => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : "TOGGLE",
                DeckActionType.MultiAction => "MULTI",
                DeckActionType.Delay => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{ActionParameter}MS" : "DELAY",
                DeckActionType.SystemSleep => "SLEEP",
                DeckActionType.SystemShutdown => "SHUTDOWN",
                DeckActionType.SystemRestart => "RESTART",
                DeckActionType.SystemLockScreen => "LOCK",
                DeckActionType.SystemScreensaver => "SCREEN",
                DeckActionType.SystemVirtualDesktopNext => "VD NEXT",
                DeckActionType.SystemVirtualDesktopPrev => "VD PREV",
                DeckActionType.SystemVirtualDesktopCreate => "VD NEW",
                DeckActionType.SystemVirtualDesktopClose => "VD CLOSE",
                DeckActionType.SystemTaskView => "TASK VIEW",
                DeckActionType.SystemShowDesktop => IsActiveToggleState ? "RESTORE" : "DESKTOP",
                DeckActionType.SystemClipboardHistory => "CLIPBOARD",
                DeckActionType.ConditionalAction => "IF / ELSE",
                DeckActionType.GoToPage => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{loc.GetString("Deck_Page", "STRONA")} {ActionParameter}" : loc.GetString("Deck_Page", "STRONA"),
                DeckActionType.GoToProfile => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter.ToUpperInvariant() : loc.GetString("Deck_Profile", "PROFIL"),
                DeckActionType.OpenFolder => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter.ToUpperInvariant() : loc.GetString("Deck_Folder", "FOLDER"),
                DeckActionType.GoBack => loc.GetString("Deck_Back", "WRÓĆ"),
                DeckActionType.GoHome => loc.GetString("Deck_Home", "GŁÓWNA"),
                DeckActionType.NextPage => loc.GetString("Deck_Next", "NASTĘPNA"),
                DeckActionType.PreviousPage => loc.GetString("Deck_Prev", "POPRZEDNIA"),
                DeckActionType.DisplayCpuUsage => "CPU %",
                DeckActionType.DisplayRamUsage => "RAM",
                DeckActionType.DisplayGpuUsage => "GPU %",
                DeckActionType.DisplayCpuTemp => "CPU °C",
                DeckActionType.DisplayGpuTemp => "GPU °C",
                DeckActionType.DisplayNetworkSpeed => loc.GetString("Deck_Net", "SIEĆ"),
                DeckActionType.DisplayDiskUsage => string.IsNullOrWhiteSpace(ActionParameter)
                    ? loc.GetString("Deck_Disk", "DYSK")
                    : $"{loc.GetString("Deck_Disk", "DYSK")} {ActionParameter.TrimEnd('\\', '/')}",
                DeckActionType.DisplayClock => "CLOCK",
                DeckActionType.DisplayDate => "DATE",
                DeckActionType.NowPlaying => "MUSIC",
                DeckActionType.DisplayFps => "FPS",
                DeckActionType.ObsToggleStream => loc.GetString("Deck_Stream", "STREAM"),
                DeckActionType.ObsStartStream => $"{loc.GetString("Deck_Stream", "STREAM")} ON",
                DeckActionType.ObsStopStream => $"{loc.GetString("Deck_Stream", "STREAM")} OFF",
                DeckActionType.ObsToggleRecord => loc.GetString("Deck_Record", "REC"),
                DeckActionType.ObsStartRecord => $"{loc.GetString("Deck_Record", "REC")} ON",
                DeckActionType.ObsStopRecord => $"{loc.GetString("Deck_Record", "REC")} OFF",
                DeckActionType.ObsToggleVirtualCam => "VCAM",
                DeckActionType.ObsSaveReplayBuffer => "REPLAY",
                DeckActionType.ObsSceneSwitch => !string.IsNullOrWhiteSpace(ObsSceneName) ? ObsSceneName : (!string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : loc.GetString("Deck_Scene", "SCENA")),
                DeckActionType.ObsStudioModeToggle => "STUDIO",
                DeckActionType.ObsTriggerTransition => loc.GetString("Deck_Transition", "PRZEJŚCIE"),
                DeckActionType.TwitchChatEmoteOnly => "EMOTE",
                DeckActionType.TwitchChatSlowMode => "SLOW",
                DeckActionType.TwitchChatSubOnly => "SUBS",
                DeckActionType.TwitchClearChat => "CLEAR",
                DeckActionType.TwitchSendMessage => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter) : "CHAT",
                DeckActionType.TwitchCreateClip => "CLIP",
                DeckActionType.TwitchCreateMarker => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter) : "MARKER",
                DeckActionType.TwitchStartRaid => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter.ToUpperInvariant()) : "RAID",
                DeckActionType.TwitchCancelRaid => "UNRAID",
                DeckActionType.TwitchRunCommercial => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{ActionParameter}S" : "ADS",
                DeckActionType.KickChatEmoteOnly => "EMOTE",
                DeckActionType.KickChatSlowMode => "SLOW",
                DeckActionType.KickChatSubOnly => "SUBS",
                DeckActionType.KickClearChat => "CLEAR",
                DeckActionType.KickSendMessage => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter) : "CHAT",
                DeckActionType.KickCreateClip => "CLIP",
                DeckActionType.SpotifyToggleShuffle => IsActiveToggleState ? "SHUF ON" : "SHUF OFF",
                DeckActionType.SpotifyToggleRepeat => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : (IsActiveToggleState ? "REPEAT" : "REP OFF"),
                DeckActionType.SpotifyLikeTrack => IsActiveToggleState ? "LIKED" : "LIKE",
                DeckActionType.SpotifyAddToQueue => "QUEUE",
                DeckActionType.SpotifyPlayUri => "PLAY",
                DeckActionType.VlcPlayPause => IsActiveToggleState ? "PAUSE" : "PLAY",
                DeckActionType.VlcStop => "STOP",
                DeckActionType.VlcNext => "NEXT",
                DeckActionType.VlcPrevious => "PREV",
                DeckActionType.VlcFullscreenToggle => IsActiveToggleState ? "WINDOW" : "FULL",
                DeckActionType.VoicemodVoiceSelect => GetVoicemodDisplayTitle(SelectedVoicemodVoice?.Name ?? ActionParameter, "VOICE"),
                DeckActionType.VoicemodVoiceToggle => IsActiveToggleState ? "VC ON" : "VC OFF",
                DeckActionType.VoicemodMuteVoice => IsActiveToggleState ? "VM MUTED" : "VM MIC",
                DeckActionType.VoicemodHearMyVoiceToggle => IsActiveToggleState ? "HEAR ON" : "HEAR OFF",
                DeckActionType.VoicemodBackgroundToggle => IsActiveToggleState ? "BG FX ON" : "BG FX OFF",
                DeckActionType.VoicemodPlaySound => GetVoicemodDisplayTitle(SelectedVoicemodSound?.Name ?? ActionParameter, "SOUND"),
                DeckActionType.VoicemodStopAllSounds => "STOP SOUND",
                DeckActionType.JetKvmPowerShort => IsActiveToggleState ? "SRV ON" : "SRV OFF",
                DeckActionType.JetKvmPowerLong => "PWR CUT",
                DeckActionType.JetKvmReset => "RESET",
                DeckActionType.JetKvmPowerStatus => IsActiveToggleState ? "SRV ON" : "SRV OFF",
                DeckActionType.YouTubeLiveViewers => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter.ToUpperInvariant()) : "YT LIVE",
                DeckActionType.StreamlabsSceneSwitch => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : "SL SCENE",
                DeckActionType.DiscordMuteToggle => "DC MUTE",
                DeckActionType.DiscordDeafenToggle => "DC DEAF",
                DeckActionType.DiscordCameraToggle => "DC CAM",
                DeckActionType.DiscordScreenShareToggle => "DC STREAM",
                DeckActionType.DiscordPushToTalk => "DC PTT",
                DeckActionType.DiscordJoinVoiceChannel => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter.ToUpperInvariant()) : "DC VOICE",
                DeckActionType.TeamsMuteToggle => "TEAMS MUTE",
                DeckActionType.TeamsCameraToggle => "TEAMS CAM",
                DeckActionType.TeamsRaiseHand => "TEAMS HAND",
                DeckActionType.TeamsLeaveMeeting => "TEAMS LEAVE",
                DeckActionType.RazerDpiCycle => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{ActionParameter}\nDPI" : "DPI CYCLE",
                DeckActionType.RazerDpiUp => "DPI +",
                DeckActionType.RazerDpiDown => "DPI -",
                DeckActionType.RazerDpi400 => "400 DPI",
                DeckActionType.RazerDpi800 => "800 DPI",
                DeckActionType.RazerDpi1600 => "1600 DPI",
                DeckActionType.RazerDpi3200 => "3200 DPI",
                DeckActionType.RazerDpi6400 => "6400 DPI",
                DeckActionType.RazerProfileNext => $"{loc.GetString("Deck_Profile", "PROFIL")} +",
                DeckActionType.RazerProfilePrev => $"{loc.GetString("Deck_Profile", "PROFIL")} -",
                DeckActionType.RazerProfile1 => $"{loc.GetString("Deck_Profile", "PROFIL")} #1",
                DeckActionType.RazerProfile2 => $"{loc.GetString("Deck_Profile", "PROFIL")} #2",
                DeckActionType.RazerProfile3 => $"{loc.GetString("Deck_Profile", "PROFIL")} #3",
                DeckActionType.RazerProfile4 => $"{loc.GetString("Deck_Profile", "PROFIL")} #4",
                DeckActionType.RazerProfile5 => $"{loc.GetString("Deck_Profile", "PROFIL")} #5",
                DeckActionType.RazerPollingRateCycle => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{ActionParameter}\nHz" : "HZ CYCLE",
                DeckActionType.RazerPollingRateUp => "HZ +",
                DeckActionType.RazerPollingRateDown => "HZ -",
                DeckActionType.RazerPollingRate125 => "125 Hz",
                DeckActionType.RazerPollingRate500 => "500 Hz",
                DeckActionType.RazerPollingRate1000 => "1000 Hz",
                DeckActionType.RazerPollingRate2000 => "2000 Hz",
                DeckActionType.RazerPollingRate4000 => "4000 Hz",
                DeckActionType.RazerPollingRate8000 => "8000 Hz",
                DeckActionType.WootingProfileCycle => "WOOT CYCLE",
                DeckActionType.WootingProfileNext => "WOOT PROF+",
                DeckActionType.WootingProfilePrev => "WOOT PROF-",
                DeckActionType.WootingProfileDigital => "WOOT DIGI",
                DeckActionType.WootingProfile1 => "WOOT #1",
                DeckActionType.WootingProfile2 => "WOOT #2",
                DeckActionType.WootingProfile3 => "WOOT #3",
                DeckActionType.HomeAssistantToggle => !string.IsNullOrWhiteSpace(ActionParameter) ? GetHomeAssistantDisplayTitle(ActionParameter) : "HA TOGGLE",
                DeckActionType.HomeAssistantTurnOn => !string.IsNullOrWhiteSpace(ActionParameter) ? GetHomeAssistantDisplayTitle(ActionParameter) : "HA ON",
                DeckActionType.HomeAssistantTurnOff => !string.IsNullOrWhiteSpace(ActionParameter) ? GetHomeAssistantDisplayTitle(ActionParameter) : "HA OFF",
                DeckActionType.HomeAssistantCallService => !string.IsNullOrWhiteSpace(ActionParameter) ? GetHomeAssistantDisplayTitle(ActionParameter) : "HA SERVICE",
                _ => AssignedAction.ToString()
            };
        }
    }

    private static string GetHomeAssistantDisplayTitle(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return "HOME ASSISTANT";
        var parts = entityId.Split('.');
        string name = parts.Length > 1 ? parts[1] : entityId;
        name = name.Replace('_', ' ');
        return name.Length > 10 ? name.Substring(0, 10).ToUpperInvariant() : name.ToUpperInvariant();
    }

    private static string GetVoicemodDisplayTitle(string? param, string fallback)
    {
        if (string.IsNullOrWhiteSpace(param)) return fallback;
        return param.Length > 8 ? param.Substring(0, 8) + ".." : param.ToUpperInvariant();
    }

    public string DisplaySubtitle
    {
        get
        {
            if (IsMonitoringAction && !string.IsNullOrWhiteSpace(_liveValueText)) return _liveValueText;
            if (IsActiveToggleState && !string.IsNullOrWhiteSpace(ActiveCustomSubtitle)) return ActiveCustomSubtitle;
            if (!string.IsNullOrWhiteSpace(CustomSubtitle)) return CustomSubtitle;
            if (!string.IsNullOrWhiteSpace(_liveValueText)) return _liveValueText;
            var loc = LocalizationService.Instance;
            return AssignedAction switch
            {
                DeckActionType.None => "",
                DeckActionType.MediaPlayPause => IsActiveToggleState ? loc.GetString("Sub_Paused", "● PAUZA") : loc.GetString("Sub_Playing", "ODTWARZANIE"),
                DeckActionType.MediaNext => loc.GetString("Sub_NextTrack", "Następny utwór"),
                DeckActionType.MediaPrevious => loc.GetString("Sub_PrevTrack", "Poprzedni utwór"),
                DeckActionType.MediaStop => loc.GetString("Sub_Stop", "Zatrzymaj"),
                DeckActionType.MasterMute => IsActiveToggleState ? loc.GetString("Sub_Muted", "● WYCISZONY") : loc.GetString("Sub_ToggleMute", "Głośność"),
                DeckActionType.MicMute => IsActiveToggleState ? loc.GetString("Sub_Muted", "● WYCISZONY") : loc.GetString("Sub_ToggleMic", "Mikrofon"),
                DeckActionType.VolumeUp => loc.GetString("Sub_VolUp", "Głośniej (+2%)"),
                DeckActionType.VolumeDown => loc.GetString("Sub_VolDown", "Ciszej (-2%)"),
                DeckActionType.Screenshot => "Win+Shift+S",
                DeckActionType.TaskManager => "Ctrl+Shift+Esc",
                DeckActionType.Calculator => "calc.exe",
                DeckActionType.Browser => loc.GetString("Sub_Browser", "Przeglądarka"),
                DeckActionType.OpenApplication => loc.GetString("Sub_LaunchApp", "Uruchom program"),
                DeckActionType.OpenWebsite => loc.GetString("Sub_Website", "Strona WWW"),
                DeckActionType.OpenFile => loc.GetString("Sub_FileFolder", "Plik lub folder"),
                DeckActionType.CloseApplication => loc.GetString("Sub_CloseApp", "Zamknij program"),
                DeckActionType.MultiAction => loc.GetString("Sub_MultiAction", "Sekwencja akcji"),
                DeckActionType.Delay => loc.GetString("Sub_Delay", "Pauza / Opóźnienie"),
                DeckActionType.TextPaste => loc.GetString("Sub_TextPaste", "Wklej tekst"),
                DeckActionType.Hotkey => loc.GetString("Sub_Hotkey", "Skrót klawiszy"),
                DeckActionType.HotkeySwitch => IsActiveToggleState ? loc.GetString("Sub_HotkeyState2", "Skrót #2") : loc.GetString("Sub_HotkeyState1", "Skrót #1"),
                DeckActionType.SystemSleep => loc.GetString("Sub_Sleep", "Uśpij komputer"),
                DeckActionType.SystemShutdown => loc.GetString("Sub_Shutdown", "Wyłącz komputer"),
                DeckActionType.SystemRestart => loc.GetString("Sub_Restart", "Restart komputera"),
                DeckActionType.SystemLockScreen => "Win + L",
                DeckActionType.SystemScreensaver => loc.GetString("Sub_Screensaver", "Wygaszacz"),
                DeckActionType.SystemVirtualDesktopNext => loc.GetString("Sub_VirtualDesktopNext", "Następny pulpit"),
                DeckActionType.SystemVirtualDesktopPrev => loc.GetString("Sub_VirtualDesktopPrev", "Poprzedni pulpit"),
                DeckActionType.SystemVirtualDesktopCreate => loc.GetString("Sub_VirtualDesktopCreate", "Nowy pulpit"),
                DeckActionType.SystemVirtualDesktopClose => loc.GetString("Sub_VirtualDesktopClose", "Zamknij pulpit"),
                DeckActionType.SystemTaskView => loc.GetString("Sub_TaskView", "Widok zadań"),
                DeckActionType.SystemShowDesktop => IsActiveToggleState ? loc.GetString("Sub_RestoreWindows", "Przywróć okna") : loc.GetString("Sub_ShowDesktop", "Pokaż pulpit"),
                DeckActionType.SystemClipboardHistory => loc.GetString("Sub_ClipboardHistory", "Historia schowka"),
                DeckActionType.ConditionalAction => loc.GetString("Sub_ConditionalAction", "Akcja warunkowa"),
                DeckActionType.GoToPage => loc.GetString("Sub_GoToPage", "Przejdź do strony"),
                DeckActionType.GoToProfile => loc.GetString("Sub_GoToProfile", "Zmień profil"),
                DeckActionType.OpenFolder => loc.GetString("Sub_OpenFolder", "Otwórz folder"),
                DeckActionType.GoBack => loc.GetString("Sub_GoBack", "Wróć poziom wyżej"),
                DeckActionType.GoHome => loc.GetString("Sub_GoHome", "Strona główna"),
                DeckActionType.NextPage => loc.GetString("Sub_NextPage", "Następna strona"),
                DeckActionType.PreviousPage => loc.GetString("Sub_PrevPage", "Poprzednia strona"),
                DeckActionType.DisplayCpuUsage => loc.GetString("Sub_CpuUsage", "Użycie CPU"),
                DeckActionType.DisplayRamUsage => loc.GetString("Sub_RamUsage", "Użycie RAM"),
                DeckActionType.DisplayGpuUsage => loc.GetString("Sub_GpuUsage", "Użycie GPU"),
                DeckActionType.DisplayCpuTemp => loc.GetString("Sub_CpuTemp", "Temperatura CPU"),
                DeckActionType.DisplayGpuTemp => loc.GetString("Sub_GpuTemp", "Temperatura GPU"),
                DeckActionType.DisplayNetworkSpeed => loc.GetString("Sub_NetSpeed", "Prędkość sieci"),
                DeckActionType.DisplayDiskUsage => loc.GetString("Sub_DiskUsage", "Zajętość dysku"),
                DeckActionType.DisplayClock => loc.GetString("Sub_Clock", "Czas systemowy"),
                DeckActionType.DisplayDate => loc.GetString("Sub_Date", "Data kalendarza"),
                DeckActionType.NowPlaying => loc.GetString("Sub_NowPlaying", "Teraz odtwarzane"),
                DeckActionType.DisplayFps => loc.GetString("Sub_Fps", "Licznik FPS"),
                DeckActionType.ObsToggleStream => IsActiveToggleState ? loc.GetString("Sub_ObsStreamingActive", "● NA ŻYWO") : loc.GetString("Sub_ObsToggleStream", "Przełącz stream"),
                DeckActionType.ObsStartStream => loc.GetString("Sub_ObsStartStream", "Włącz stream"),
                DeckActionType.ObsStopStream => loc.GetString("Sub_ObsStopStream", "Wyłącz stream"),
                DeckActionType.ObsToggleRecord => IsActiveToggleState ? loc.GetString("Sub_ObsRecordingActive", "● NAGRYWANIE") : loc.GetString("Sub_ObsToggleRecord", "Przełącz nagrywanie"),
                DeckActionType.ObsStartRecord => loc.GetString("Sub_ObsStartRecord", "Włącz nagrywanie"),
                DeckActionType.ObsStopRecord => loc.GetString("Sub_ObsStopRecord", "Wyłącz nagrywanie"),
                DeckActionType.ObsToggleVirtualCam => IsActiveToggleState ? loc.GetString("Sub_ObsVirtualCamActive", "● VCAM WŁ") : loc.GetString("Sub_ObsVirtualCam", "Kamera wirtualna"),
                DeckActionType.ObsSaveReplayBuffer => loc.GetString("Sub_ObsReplay", "Zapisz powtórkę"),
                DeckActionType.ObsSceneSwitch => loc.GetString("Sub_ObsScene", "Scena OBS"),
                DeckActionType.ObsStudioModeToggle => IsActiveToggleState ? loc.GetString("Sub_ObsStudioActive", "● STUDIO WŁ") : loc.GetString("Sub_ObsStudio", "Tryb studyjny"),
                DeckActionType.ObsTriggerTransition => loc.GetString("Sub_ObsTransition", "Wyzwalacz przejścia"),
                DeckActionType.TwitchChatEmoteOnly => IsActiveToggleState ? loc.GetString("Sub_TwitchEmoteActive", "● TYLKO EMOTKI") : loc.GetString("Sub_TwitchEmote", "Tylko emotki"),
                DeckActionType.TwitchChatSlowMode => IsActiveToggleState ? loc.GetString("Sub_TwitchSlowActive", "● TRYB POWOLNY") : loc.GetString("Sub_TwitchSlow", "Tryb powolny"),
                DeckActionType.TwitchChatSubOnly => IsActiveToggleState ? loc.GetString("Sub_TwitchSubActive", "● DLA SUBÓW") : loc.GetString("Sub_TwitchSub", "Dla subów"),
                DeckActionType.TwitchClearChat => loc.GetString("Sub_TwitchClear", "Wyczyść czat"),
                DeckActionType.TwitchSendMessage => loc.GetString("Sub_TwitchSend", "Wyślij wiadomość"),
                DeckActionType.TwitchCreateClip => loc.GetString("Sub_TwitchClip", "Utwórz klip"),
                DeckActionType.TwitchCreateMarker => loc.GetString("Sub_TwitchMarker", "Znacznik streamu"),
                DeckActionType.TwitchStartRaid => loc.GetString("Sub_TwitchRaid", "Rozpocznij Raid"),
                DeckActionType.TwitchCancelRaid => loc.GetString("Sub_TwitchCancelRaid", "Anuluj Raid"),
                DeckActionType.TwitchRunCommercial => loc.GetString("Sub_TwitchAd", "Przerwa reklamowa"),
                DeckActionType.KickChatEmoteOnly => IsActiveToggleState ? loc.GetString("Sub_KickEmoteActive", "● TYLKO EMOTKI") : loc.GetString("Sub_KickEmote", "Tylko emotki"),
                DeckActionType.KickChatSlowMode => IsActiveToggleState ? loc.GetString("Sub_KickSlowActive", "● TRYB POWOLNY") : loc.GetString("Sub_KickSlow", "Tryb powolny"),
                DeckActionType.KickChatSubOnly => IsActiveToggleState ? loc.GetString("Sub_KickSubActive", "● DLA SUBÓW") : loc.GetString("Sub_KickSub", "Dla subów"),
                DeckActionType.KickClearChat => loc.GetString("Sub_KickClear", "Wyczyść czat"),
                DeckActionType.KickSendMessage => loc.GetString("Sub_KickSend", "Wyślij wiadomość"),
                DeckActionType.KickCreateClip => loc.GetString("Sub_KickClip", "Utwórz klip"),
                DeckActionType.SpotifyToggleShuffle => IsActiveToggleState ? loc.GetString("Sub_SpotifyShuffleOn", "● Losowo") : loc.GetString("Sub_SpotifyShuffleOff", "Wyłączone"),
                DeckActionType.SpotifyToggleRepeat => IsActiveToggleState ? loc.GetString("Sub_SpotifyRepeatOn", "● Powtarzanie") : loc.GetString("Sub_SpotifyRepeatOff", "Wyłączone"),
                DeckActionType.SpotifyLikeTrack => IsActiveToggleState ? loc.GetString("Sub_SpotifyLiked", "💚 W ulubionych") : loc.GetString("Sub_SpotifyLike", "Polub utwór"),
                DeckActionType.SpotifyAddToQueue => loc.GetString("Sub_SpotifyAddToQueue", "Dodaj do kolejki"),
                DeckActionType.SpotifyPlayUri => loc.GetString("Sub_SpotifyPlayUri", "Uruchom playlistę"),
                DeckActionType.VlcPlayPause => IsActiveToggleState ? loc.GetString("Sub_VlcPlaying", "● Odtwarzanie") : loc.GetString("Sub_VlcPaused", "Wstrzymano"),
                DeckActionType.VlcStop => loc.GetString("Sub_VlcStop", "Zatrzymaj VLC"),
                DeckActionType.VlcNext => loc.GetString("Sub_VlcNext", "Następny utwór"),
                DeckActionType.VlcPrevious => loc.GetString("Sub_VlcPrev", "Poprzedni utwór"),
                DeckActionType.VlcFullscreenToggle => IsActiveToggleState ? loc.GetString("Sub_VlcFullscreenOn", "● Pełny ekran") : loc.GetString("Sub_VlcFullscreenOff", "Tryb okna"),
                DeckActionType.VoicemodVoiceSelect => !string.IsNullOrWhiteSpace(ActionParameter) ? (SelectedVoicemodVoice?.Name ?? ActionParameter) : loc.GetString("Sub_VoicemodSelect", "Wybierz głos"),
                DeckActionType.VoicemodVoiceToggle => IsActiveToggleState ? loc.GetString("Sub_VoicemodVcOn", "● Modulacja WŁ") : loc.GetString("Sub_VoicemodVcOff", "Modulacja WYŁ"),
                DeckActionType.VoicemodMuteVoice => IsActiveToggleState ? loc.GetString("Sub_VoicemodMuted", "● MIKROFON WYCISZONY") : loc.GetString("Sub_VoicemodUnmuted", "Mikrofon aktywny"),
                DeckActionType.VoicemodHearMyVoiceToggle => IsActiveToggleState ? loc.GetString("Sub_VoicemodHearOn", "● Odsłuch WŁ") : loc.GetString("Sub_VoicemodHearOff", "Odsłuch WYŁ"),
                DeckActionType.VoicemodBackgroundToggle => IsActiveToggleState ? loc.GetString("Sub_VoicemodBgOn", "● DŹWIĘK TŁA WŁ") : loc.GetString("Sub_VoicemodBgOff", "DŹWIĘK TŁA WYŁ"),
                DeckActionType.VoicemodPlaySound => !string.IsNullOrWhiteSpace(ActionParameter) ? (SelectedVoicemodSound?.Name ?? ActionParameter) : loc.GetString("Sub_VoicemodPlaySound", "Odtwórz dźwięk"),
                DeckActionType.VoicemodStopAllSounds => loc.GetString("Sub_VoicemodStopAllSounds", "Zatrzymaj dźwięki"),
                DeckActionType.JetKvmPowerShort => IsActiveToggleState ? loc.GetString("Sub_JetKvmPowerOn", "● SERWER WŁ") : loc.GetString("Sub_JetKvmPowerShort", "Włącz / ACPI"),
                DeckActionType.JetKvmPowerLong => loc.GetString("Sub_JetKvmPowerLong", "Wymuszone (5s)"),
                DeckActionType.JetKvmReset => loc.GetString("Sub_JetKvmReset", "Reset sprzętowy"),
                DeckActionType.JetKvmPowerStatus => IsActiveToggleState ? loc.GetString("Sub_JetKvmPowerOn", "● SERWER WŁ") : loc.GetString("Sub_JetKvmPowerOff", "● SERWER WYŁ"),
                DeckActionType.YouTubeLiveViewers => loc.GetString("Sub_YtViewers", "Widzowie YouTube"),
                DeckActionType.StreamlabsSceneSwitch => IsActiveToggleState ? loc.GetString("Sub_Active", "● AKTYWNA") : "Streamlabs",
                DeckActionType.DiscordMuteToggle => IsActiveToggleState ? loc.GetString("Sub_Muted", "● WYCISZONY") : "Discord Mic",
                DeckActionType.DiscordDeafenToggle => IsActiveToggleState ? loc.GetString("Sub_Deafened", "● GŁUCHY") : "Discord Audio",
                DeckActionType.DiscordCameraToggle => IsActiveToggleState ? loc.GetString("Sub_CamOn", "● KAMERA WŁ") : "Discord Video",
                DeckActionType.DiscordScreenShareToggle => IsActiveToggleState ? loc.GetString("Sub_GoLive", "● EKRAN WŁ") : "Discord Screen",
                DeckActionType.DiscordPushToTalk => IsActiveToggleState ? loc.GetString("Sub_Talk", "● NADAWANIE") : "Push-To-Talk",
                DeckActionType.DiscordJoinVoiceChannel => IsActiveToggleState ? loc.GetString("Sub_Connected", "● POŁĄCZONO") : loc.GetString("Sub_JoinVoice", "Dołącz do kanału"),
                DeckActionType.TeamsMuteToggle => IsActiveToggleState ? loc.GetString("Sub_Muted", "● WYCISZONY") : "Ctrl+Shift+M",
                DeckActionType.TeamsCameraToggle => IsActiveToggleState ? loc.GetString("Sub_CamOn", "● KAMERA WŁ") : "Ctrl+Shift+O",
                DeckActionType.TeamsRaiseHand => IsActiveToggleState ? loc.GetString("Sub_HandUp", "● RĘKA W GÓRZE") : "Ctrl+Shift+K",
                DeckActionType.TeamsLeaveMeeting => "Ctrl+Shift+H",
                DeckActionType.RazerDpiCycle => loc.GetString("Sub_RazerSensor", "Sensor Razer"),
                DeckActionType.RazerDpiUp => loc.GetString("Sub_RazerDpiUp", "Wyższy etap DPI"),
                DeckActionType.RazerDpiDown => loc.GetString("Sub_RazerDpiDown", "Niższy etap DPI"),
                DeckActionType.RazerProfileNext or DeckActionType.RazerProfilePrev => loc.GetString("Sub_MouseMemory", "Pamięć myszy"),
                DeckActionType.RazerPollingRateCycle => loc.GetString("Sub_PollingRate", "Częstotliwość"),
                DeckActionType.WootingProfileCycle or DeckActionType.WootingProfileNext or DeckActionType.WootingProfilePrev => loc.GetString("Sub_WootingKeyboard", "Klawiatura"),
                DeckActionType.WootingProfileDigital => loc.GetString("Sub_DigitalProfile", "Profil Cyfrowy"),
                DeckActionType.WootingProfile1 => loc.GetString("Sub_AnalogProfile1", "Profil Analogowy 1"),
                DeckActionType.WootingProfile2 => loc.GetString("Sub_AnalogProfile2", "Profil Analogowy 2"),
                DeckActionType.WootingProfile3 => loc.GetString("Sub_AnalogProfile3", "Profil Analogowy 3"),
                DeckActionType.HomeAssistantToggle => !string.IsNullOrWhiteSpace(ActionParameter)
                    ? (IsActiveToggleState ? loc.GetString("Sub_HaOn", "● WŁĄCZONE") : loc.GetString("Sub_HaOff", "WYŁĄCZONE"))
                    : loc.GetString("Action_HomeAssistantToggle", "Przełącz stan"),
                DeckActionType.HomeAssistantTurnOn => loc.GetString("Action_HomeAssistantTurnOn", "Włącz urządzenie"),
                DeckActionType.HomeAssistantTurnOff => loc.GetString("Action_HomeAssistantTurnOff", "Wyłącz urządzenie"),
                DeckActionType.HomeAssistantCallService => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : loc.GetString("Action_HomeAssistantCallService", "Wywołaj usługę"),
                _ => ""
            };
        }
    }

    public string ActionIcon => AssignedAction switch
    {
        DeckActionType.None => "⬜",
        DeckActionType.MediaPlayPause => IsActiveToggleState ? "⏸️" : "▶️",
        DeckActionType.MediaNext => "⏭️",
        DeckActionType.MediaPrevious => "⏮️",
        DeckActionType.MediaStop => "⏹️",
        DeckActionType.MasterMute => IsActiveToggleState ? "🔇" : "🔊",
        DeckActionType.MicMute => IsActiveToggleState ? "🔇" : "🎙️",
        DeckActionType.VolumeUp => "🔊",
        DeckActionType.VolumeDown => "🔉",
        DeckActionType.Screenshot => "📸",
        DeckActionType.TaskManager => "📊",
        DeckActionType.Calculator => "🧮",
        DeckActionType.Browser => "🌐",
        DeckActionType.OpenApplication => "🚀",
        DeckActionType.OpenWebsite => "🌐",
        DeckActionType.OpenFile => "📂",
        DeckActionType.CloseApplication => "❌",
        DeckActionType.MultiAction => "⚡",
        DeckActionType.Delay => "⏳",
        DeckActionType.TextPaste => "📋",
        DeckActionType.Hotkey => "⌨️",
        DeckActionType.HotkeySwitch => IsActiveToggleState ? "2️⃣" : "1️⃣",
        DeckActionType.SystemSleep => "💤",
        DeckActionType.SystemShutdown => "⏻",
        DeckActionType.SystemRestart => "🔄",
        DeckActionType.SystemLockScreen => "🔒",
        DeckActionType.SystemScreensaver => "🖥️",
        DeckActionType.SystemVirtualDesktopNext => "🗔",
        DeckActionType.SystemVirtualDesktopPrev => "🖥️",
        DeckActionType.SystemVirtualDesktopCreate => "➕",
        DeckActionType.SystemVirtualDesktopClose => "✖️",
        DeckActionType.SystemTaskView => "🗂️",
        DeckActionType.SystemShowDesktop => IsActiveToggleState ? "🗗" : "🖥️",
        DeckActionType.SystemClipboardHistory => "📋",
        DeckActionType.ConditionalAction => "🔀",
        DeckActionType.GoToPage => "📄",
        DeckActionType.GoToProfile => "🔀",
        DeckActionType.OpenFolder => "📁",
        DeckActionType.GoBack => "⬅️",
        DeckActionType.GoHome => "🏠",
        DeckActionType.NextPage => "▶",
        DeckActionType.PreviousPage => "◀",
        DeckActionType.DisplayCpuUsage => "📊",
        DeckActionType.DisplayRamUsage => "🧠",
        DeckActionType.DisplayGpuUsage => "🎮",
        DeckActionType.DisplayCpuTemp => "🌡️",
        DeckActionType.DisplayGpuTemp => "🌡️",
        DeckActionType.DisplayNetworkSpeed => "📡",
        DeckActionType.DisplayDiskUsage => "💾",
        DeckActionType.DisplayClock => "🕐",
        DeckActionType.DisplayDate => "📅",
        DeckActionType.NowPlaying => "🎵",
        DeckActionType.DisplayFps => "🎯",
        DeckActionType.ObsToggleStream or DeckActionType.ObsStartStream or DeckActionType.ObsStopStream => IsActiveToggleState ? "🔴" : "📡",
        DeckActionType.ObsToggleRecord or DeckActionType.ObsStartRecord or DeckActionType.ObsStopRecord => IsActiveToggleState ? "⏹️" : "🔴",
        DeckActionType.ObsToggleVirtualCam => IsActiveToggleState ? "🟢" : "📹",
        DeckActionType.ObsSaveReplayBuffer => "💾",
        DeckActionType.ObsSceneSwitch => "🎬",
        DeckActionType.ObsStudioModeToggle => IsActiveToggleState ? "🎬" : "🎭",
        DeckActionType.ObsTriggerTransition => "🔀",
        DeckActionType.TwitchChatEmoteOnly => IsActiveToggleState ? "🔒" : "😀",
        DeckActionType.TwitchChatSlowMode => IsActiveToggleState ? "⏱️" : "🐢",
        DeckActionType.TwitchChatSubOnly => IsActiveToggleState ? "🌟" : "⭐",
        DeckActionType.TwitchClearChat => "🧹",
        DeckActionType.TwitchSendMessage => "💬",
        DeckActionType.TwitchCreateClip => "🎬",
        DeckActionType.TwitchCreateMarker => "📍",
        DeckActionType.TwitchStartRaid => "🚀",
        DeckActionType.TwitchCancelRaid => "🛑",
        DeckActionType.TwitchRunCommercial => "📺",
        DeckActionType.KickChatEmoteOnly => IsActiveToggleState ? "🔒" : "😀",
        DeckActionType.KickChatSlowMode => IsActiveToggleState ? "⏱️" : "🐢",
        DeckActionType.KickChatSubOnly => IsActiveToggleState ? "🌟" : "⭐",
        DeckActionType.KickClearChat => "🧹",
        DeckActionType.KickSendMessage => "💬",
        DeckActionType.KickCreateClip => "🎬",
        DeckActionType.SpotifyToggleShuffle => "🔀",
        DeckActionType.SpotifyToggleRepeat => "🔁",
        DeckActionType.SpotifyLikeTrack => IsActiveToggleState ? "💚" : "🤍",
        DeckActionType.SpotifyAddToQueue => "➕",
        DeckActionType.SpotifyPlayUri => "▶️",
        DeckActionType.VlcPlayPause => IsActiveToggleState ? "⏸️" : "▶️",
        DeckActionType.VlcStop => "⏹️",
        DeckActionType.VlcNext => "⏭️",
        DeckActionType.VlcPrevious => "⏮️",
        DeckActionType.VlcFullscreenToggle => IsActiveToggleState ? "🗗" : "⛶",
        DeckActionType.VoicemodVoiceSelect => "🤖",
        DeckActionType.VoicemodVoiceToggle => IsActiveToggleState ? "🎙️" : "🗣️",
        DeckActionType.VoicemodMuteVoice => IsActiveToggleState ? "🔇" : "🎙️",
        DeckActionType.VoicemodHearMyVoiceToggle => IsActiveToggleState ? "🎧" : "🔕",
        DeckActionType.VoicemodBackgroundToggle => IsActiveToggleState ? "🎧" : "🔇",
        DeckActionType.VoicemodPlaySound => "🔊",
        DeckActionType.VoicemodStopAllSounds => "⏹️",
        DeckActionType.JetKvmPowerShort => "🔌",
        DeckActionType.JetKvmPowerLong => "⏻",
        DeckActionType.JetKvmReset => "🔄",
        DeckActionType.JetKvmPowerStatus => IsActiveToggleState ? "💡" : "🔌",
        DeckActionType.YouTubeLiveViewers => "▶️",
        DeckActionType.StreamlabsSceneSwitch => "🎥",
        DeckActionType.DiscordMuteToggle => IsActiveToggleState ? "🔇" : "🎙️",
        DeckActionType.DiscordDeafenToggle => IsActiveToggleState ? "🔕" : "🎧",
        DeckActionType.DiscordCameraToggle => IsActiveToggleState ? "🚫" : "📷",
        DeckActionType.DiscordScreenShareToggle => IsActiveToggleState ? "📡" : "🖥️",
        DeckActionType.DiscordPushToTalk => IsActiveToggleState ? "🗣️" : "🎙️",
        DeckActionType.DiscordJoinVoiceChannel => "🔊",
        DeckActionType.TeamsMuteToggle => IsActiveToggleState ? "🔇" : "🎙️",
        DeckActionType.TeamsCameraToggle => IsActiveToggleState ? "🚫" : "📷",
        DeckActionType.TeamsRaiseHand => IsActiveToggleState ? "🙋" : "✋",
        DeckActionType.TeamsLeaveMeeting => "📞",
        DeckActionType.RazerDpiCycle or DeckActionType.RazerDpiUp or DeckActionType.RazerDpiDown or DeckActionType.RazerDpi400 or DeckActionType.RazerDpi800 or DeckActionType.RazerDpi1600 or DeckActionType.RazerDpi3200 or DeckActionType.RazerDpi6400 => "🎯",
        DeckActionType.RazerProfileNext or DeckActionType.RazerProfilePrev or DeckActionType.RazerProfile1 or DeckActionType.RazerProfile2 or DeckActionType.RazerProfile3 or DeckActionType.RazerProfile4 or DeckActionType.RazerProfile5 => "🐍",
        DeckActionType.RazerPollingRateCycle or DeckActionType.RazerPollingRateUp or DeckActionType.RazerPollingRateDown or DeckActionType.RazerPollingRate125 or DeckActionType.RazerPollingRate500 or DeckActionType.RazerPollingRate1000 or DeckActionType.RazerPollingRate2000 or DeckActionType.RazerPollingRate4000 or DeckActionType.RazerPollingRate8000 => "⚡",
        DeckActionType.WootingProfileCycle or DeckActionType.WootingProfileNext or DeckActionType.WootingProfilePrev or DeckActionType.WootingProfileDigital or DeckActionType.WootingProfile1 or DeckActionType.WootingProfile2 or DeckActionType.WootingProfile3 => "⌨️",
        DeckActionType.HomeAssistantToggle => IsActiveToggleState ? "💡" : "⚪",
        DeckActionType.HomeAssistantTurnOn => "💡",
        DeckActionType.HomeAssistantTurnOff => "🔌",
        DeckActionType.HomeAssistantCallService => "⚙️",
        DeckActionType.StreamDeckPluginAction => "🔌",
        _ => "⚡"
    };

    public string ActionDisplayName
    {
        get
        {
            if (AssignedAction == DeckActionType.StreamDeckPluginAction)
            {
                var host = App.Services?.GetService<IStreamDeckPluginHost>();
                var actionManifest = host?.GetActionManifest(StreamDeckPluginUuid, StreamDeckActionUuid);
                var plugin = host?.GetPlugin(StreamDeckPluginUuid);
                string actName = actionManifest?.Name ?? StreamDeckActionUuid;
                string plgName = plugin?.Name ?? StreamDeckPluginUuid;
                return string.IsNullOrEmpty(actName) ? "Stream Deck Plugin" : $"{plgName}: {actName}";
            }

            string baseName = LocalizationService.Instance.GetString($"Action_{AssignedAction}", AssignedAction.ToString());
            if (!string.IsNullOrWhiteSpace(ActionParameter))
            {
                return AssignedAction switch
                {
                    DeckActionType.OpenApplication or DeckActionType.OpenFile or DeckActionType.CloseApplication =>
                        $"{baseName}: {Path.GetFileName(ActionParameter)}",
                    DeckActionType.OpenWebsite or DeckActionType.Hotkey or DeckActionType.HotkeySwitch or
                    DeckActionType.MultiAction or DeckActionType.GoToPage or DeckActionType.GoToProfile or
                    DeckActionType.OpenFolder =>
                        $"{baseName}: {ActionParameter}",
                    DeckActionType.Delay =>
                        $"{baseName}: {ActionParameter} ms",
                    DeckActionType.RazerDpiCycle =>
                        $"{baseName} ({ActionParameter} DPI)",
                    DeckActionType.RazerPollingRateCycle =>
                        $"{baseName} ({ActionParameter} Hz)",
                    _ => $"{baseName}: {ActionParameter}"
                };
            }
            if (AssignedAction == DeckActionType.ObsSceneSwitch && !string.IsNullOrWhiteSpace(ObsSceneName))
            {
                return $"{baseName}: {ObsSceneName}";
            }
            return baseName;
        }
    }

    public string ActionCategory => AssignedAction switch
    {
        DeckActionType.StreamDeckPluginAction => "Stream Deck",
        DeckActionType.None => LocalizationService.Instance.GetString("Action_None", "Puste"),
        DeckActionType.MediaPlayPause or DeckActionType.MediaNext or DeckActionType.MediaPrevious or DeckActionType.MediaStop => LocalizationService.Instance.GetString("Cat_Media", "Multimedia"),
        DeckActionType.MasterMute or DeckActionType.MicMute or DeckActionType.VolumeUp or DeckActionType.VolumeDown => LocalizationService.Instance.GetString("Cat_Audio", "Audio"),
        DeckActionType.GoToPage or DeckActionType.GoToProfile or DeckActionType.OpenFolder or DeckActionType.GoBack or DeckActionType.GoHome or DeckActionType.NextPage or DeckActionType.PreviousPage => LocalizationService.Instance.GetString("Cat_Nav", "Nawigacja"),
        DeckActionType.ObsToggleStream or DeckActionType.ObsStartStream or DeckActionType.ObsStopStream or
        DeckActionType.ObsToggleRecord or DeckActionType.ObsStartRecord or DeckActionType.ObsStopRecord or
        DeckActionType.ObsToggleVirtualCam or DeckActionType.ObsSaveReplayBuffer or DeckActionType.ObsSceneSwitch or
        DeckActionType.ObsStudioModeToggle or DeckActionType.ObsTriggerTransition or
        DeckActionType.TwitchChatEmoteOnly or DeckActionType.TwitchChatSlowMode or
        DeckActionType.TwitchChatSubOnly or DeckActionType.TwitchClearChat or
        DeckActionType.TwitchSendMessage or DeckActionType.TwitchCreateClip or
        DeckActionType.TwitchCreateMarker or DeckActionType.TwitchStartRaid or
        DeckActionType.TwitchCancelRaid or DeckActionType.TwitchRunCommercial or
        DeckActionType.KickChatEmoteOnly or DeckActionType.KickChatSlowMode or
        DeckActionType.KickChatSubOnly or DeckActionType.KickClearChat or
        DeckActionType.KickSendMessage or DeckActionType.KickCreateClip or
        DeckActionType.YouTubeLiveViewers or DeckActionType.StreamlabsSceneSwitch or
        DeckActionType.DiscordMuteToggle or DeckActionType.DiscordDeafenToggle or
        DeckActionType.DiscordCameraToggle or DeckActionType.DiscordScreenShareToggle or
        DeckActionType.DiscordPushToTalk or
        DeckActionType.DiscordJoinVoiceChannel or
        DeckActionType.TeamsMuteToggle or DeckActionType.TeamsCameraToggle or
        DeckActionType.TeamsRaiseHand or DeckActionType.TeamsLeaveMeeting => LocalizationService.Instance.GetString("Cat_Streaming", "Streaming"),
        DeckActionType.DisplayCpuUsage or DeckActionType.DisplayRamUsage or DeckActionType.DisplayGpuUsage or DeckActionType.DisplayCpuTemp or
        DeckActionType.DisplayGpuTemp or DeckActionType.DisplayNetworkSpeed or DeckActionType.DisplayDiskUsage or DeckActionType.DisplayClock or
        DeckActionType.DisplayDate or DeckActionType.NowPlaying or DeckActionType.DisplayFps => LocalizationService.Instance.GetString("Cat_Monitor", "Monitor"),
        DeckActionType.Screenshot or DeckActionType.TaskManager or DeckActionType.Calculator or DeckActionType.Browser or
        DeckActionType.OpenApplication or DeckActionType.OpenWebsite or DeckActionType.OpenFile or DeckActionType.CloseApplication or DeckActionType.TextPaste or
        DeckActionType.Hotkey or DeckActionType.HotkeySwitch or DeckActionType.MultiAction or DeckActionType.Delay or DeckActionType.SystemSleep or DeckActionType.SystemShutdown or
        DeckActionType.SystemRestart or DeckActionType.SystemLockScreen or DeckActionType.SystemScreensaver or
        DeckActionType.SystemVirtualDesktopNext or DeckActionType.SystemVirtualDesktopPrev or
        DeckActionType.SystemVirtualDesktopCreate or DeckActionType.SystemVirtualDesktopClose or
        DeckActionType.SystemTaskView or DeckActionType.SystemShowDesktop or
        DeckActionType.SystemClipboardHistory or DeckActionType.ConditionalAction => LocalizationService.Instance.GetString("Cat_System", "System"),
        _ when AssignedAction.ToString().StartsWith("JetKvm") => LocalizationService.Instance.GetString("Cat_JetKvm", "JetKVM"),
        _ when AssignedAction.ToString().StartsWith("Razer") => LocalizationService.Instance.GetString("Cat_Razer", "Razer"),
        _ when AssignedAction.ToString().StartsWith("Wooting") => LocalizationService.Instance.GetString("Cat_Wooting", "Wooting"),
        _ when AssignedAction.ToString().StartsWith("Spotify") => LocalizationService.Instance.GetString("Cat_Spotify", "Spotify"),
        _ when AssignedAction.ToString().StartsWith("Vlc") => LocalizationService.Instance.GetString("Cat_Vlc", "VLC"),
        _ when AssignedAction.ToString().StartsWith("Voicemod") => LocalizationService.Instance.GetString("Cat_Voicemod", "Voicemod"),
        _ when AssignedAction.ToString().StartsWith("HomeAssistant") => LocalizationService.Instance.GetString("Cat_SmartHome", "Smart Home"),
        _ => LocalizationService.Instance.GetString("Cat_All", "Inne")
    };

    public bool RequiresActionParameter => AssignedAction is
        DeckActionType.OpenApplication or
        DeckActionType.OpenWebsite or
        DeckActionType.OpenFile or
        DeckActionType.CloseApplication or
        DeckActionType.TextPaste or
        DeckActionType.Hotkey or
        DeckActionType.HotkeySwitch or
        DeckActionType.MultiAction or
        DeckActionType.Delay or
        DeckActionType.GoToPage or
        DeckActionType.GoToProfile or
        DeckActionType.OpenFolder or
        DeckActionType.ObsSceneSwitch or
        DeckActionType.TwitchSendMessage or
        DeckActionType.TwitchCreateMarker or
        DeckActionType.TwitchStartRaid or
        DeckActionType.TwitchRunCommercial or
        DeckActionType.KickSendMessage or
        DeckActionType.YouTubeLiveViewers or
        DeckActionType.StreamlabsSceneSwitch or
        DeckActionType.DiscordJoinVoiceChannel or
        DeckActionType.SpotifyAddToQueue or
        DeckActionType.SpotifyPlayUri or
        DeckActionType.VoicemodVoiceSelect or
        DeckActionType.VoicemodPlaySound or
        DeckActionType.HomeAssistantToggle or
        DeckActionType.HomeAssistantTurnOn or
        DeckActionType.HomeAssistantTurnOff or
        DeckActionType.HomeAssistantCallService or
        DeckActionType.ConditionalAction;

    public bool IsObsSceneAction => AssignedAction == DeckActionType.ObsSceneSwitch;
    public bool IsNotObsSceneAction => !IsObsSceneAction;

    public bool IsFileBrowseSupported => AssignedAction is
        DeckActionType.OpenApplication or
        DeckActionType.OpenFile or
        DeckActionType.CloseApplication;

    public string ActionParameterLabel
    {
        get
        {
            var loc = LocalizationService.Instance;
            return AssignedAction switch
            {
                DeckActionType.OpenApplication => loc.GetString("ParamLabel_OpenApp", "Ścieżka do aplikacji (.exe):"),
                DeckActionType.OpenWebsite => loc.GetString("ParamLabel_OpenWebsite", "Adres URL strony internetowej:"),
                DeckActionType.OpenFile => loc.GetString("ParamLabel_OpenFile", "Ścieżka do pliku lub folderu:"),
                DeckActionType.CloseApplication => loc.GetString("ParamLabel_CloseApp", "Nazwa procesu / pliku (lub puste dla aktywnego okna):"),
                DeckActionType.TextPaste => loc.GetString("ParamLabel_TextPaste", "Treść tekstu do wklejenia:"),
                DeckActionType.Hotkey => loc.GetString("ParamLabel_Hotkey", "Skrót klawiszowy (np. Ctrl+Shift+S):"),
                DeckActionType.HotkeySwitch => loc.GetString("ParamLabel_HotkeySwitch", "Przełączane skróty (np. Ctrl+M|Ctrl+Shift+M):"),
                DeckActionType.MultiAction => loc.GetString("ParamLabel_MultiAction", "Kroki sekwencji (format: akcja|parametr;delay:ms):"),
                DeckActionType.Delay => loc.GetString("ParamLabel_Delay", "Czas opóźnienia w milisekundach (ms):"),
                DeckActionType.GoToPage => loc.GetString("ParamLabel_GoToPage", "Numer strony (np. 1, 2, 3):"),
                DeckActionType.GoToProfile => loc.GetString("ParamLabel_GoToProfile", "Nazwa lub identyfikator profilu:"),
                DeckActionType.OpenFolder => loc.GetString("ParamLabel_OpenFolder", "Nazwa folderu (podmenu):"),
                DeckActionType.ObsSceneSwitch => loc.GetString("ParamLabel_ObsScene", "Nazwa sceny OBS (program):"),
                DeckActionType.TwitchSendMessage => loc.GetString("ParamLabel_TwitchMessage", "Treść wiadomości do wysłania na czat:"),
                DeckActionType.TwitchCreateMarker => loc.GetString("ParamLabel_TwitchMarker", "Opis znacznika transmisji (opcjonalny, max 140 znaków):"),
                DeckActionType.TwitchStartRaid => loc.GetString("ParamLabel_TwitchRaid", "Docelowy kanał Twitch do raidu:"),
                DeckActionType.TwitchRunCommercial => loc.GetString("ParamLabel_TwitchCommercial", "Długość reklamy w sekundach (30, 60, 90, 120, 150, 180):"),
                DeckActionType.KickSendMessage => loc.GetString("ParamLabel_KickMessage", "Treść wiadomości do wysłania na czat Kick:"),
                DeckActionType.YouTubeLiveViewers => loc.GetString("ParamLabel_YouTubeViewers", "ID kanału (UC...), @handle lub ID/URL transmisji YouTube (opcjonalnie):"),
                DeckActionType.StreamlabsSceneSwitch => loc.GetString("ParamLabel_StreamlabsScene", "Nazwa sceny w Streamlabs Desktop:"),
                DeckActionType.DiscordJoinVoiceChannel => loc.GetString("ParamLabel_DiscordVoice", "Link do kanału lub ID kanału Discord:"),
                DeckActionType.SpotifyAddToQueue => loc.GetString("ParamLabel_SpotifyQueue", "URI lub URL utworu Spotify (spotify:track:...):"),
                DeckActionType.SpotifyPlayUri => loc.GetString("ParamLabel_SpotifyPlayUri", "URI lub URL playlisty / albumu / utworu Spotify:"),
                DeckActionType.VoicemodVoiceSelect => loc.GetString("ParamLabel_VoicemodVoice", "ID lub nazwa głosu Voicemod (np. clean, robot, cave):"),
                DeckActionType.VoicemodPlaySound => loc.GetString("ParamLabel_VoicemodSound", "Nazwa pliku lub ID mema/dźwięku Voicemod (FileName):"),
                DeckActionType.HomeAssistantToggle => loc.GetString("ParamLabel_HomeAssistantToggle", "Identyfikator encji (np. light.living_room, switch.fan):"),
                DeckActionType.HomeAssistantTurnOn => loc.GetString("ParamLabel_HomeAssistantTurnOn", "Identyfikator encji do włączenia:"),
                DeckActionType.HomeAssistantTurnOff => loc.GetString("ParamLabel_HomeAssistantTurnOff", "Identyfikator encji do wyłączenia:"),
                DeckActionType.HomeAssistantCallService => loc.GetString("ParamLabel_HomeAssistantCallService", "Usługa (np. light.turn_on lub scene.turn_on):"),
                DeckActionType.ConditionalAction => loc.GetString("ParamLabel_ConditionalAction", "Reguła warunkowa (format: Cel?AkcjaTrue:AkcjaFalse lub JSON):"),
                _ => loc.GetString("ActionParam_Label", "Parametr akcji:")
            };
        }
    }

    public string ActionParameterWatermark
    {
        get
        {
            var loc = LocalizationService.Instance;
            return AssignedAction switch
            {
                DeckActionType.OpenApplication => loc.GetString("ParamWatermark_OpenApp", "np. C:\\Program Files\\App\\app.exe"),
                DeckActionType.OpenWebsite => loc.GetString("ParamWatermark_OpenWebsite", "np. https://youtube.com"),
                DeckActionType.OpenFile => loc.GetString("ParamWatermark_OpenFile", "np. D:\\Dokumenty\\plik.pdf"),
                DeckActionType.CloseApplication => loc.GetString("ParamWatermark_CloseApp", "np. notepad.exe, chrome lub puste dla bieżącego okna"),
                DeckActionType.TextPaste => loc.GetString("ParamWatermark_TextPaste", "Wpisz tekst do automatycznego wklejenia..."),
                DeckActionType.Hotkey => loc.GetString("ParamWatermark_Hotkey", "np. Ctrl+Shift+S"),
                DeckActionType.HotkeySwitch => loc.GetString("ParamWatermark_HotkeySwitch", "np. Ctrl+M|Ctrl+Shift+M"),
                DeckActionType.MultiAction => loc.GetString("ParamWatermark_MultiAction", "np. calc.exe;delay:500;notepad.exe"),
                DeckActionType.Delay => loc.GetString("ParamWatermark_Delay", "np. 500 lub 1000"),
                DeckActionType.GoToPage => loc.GetString("ParamWatermark_GoToPage", "np. 2"),
                DeckActionType.GoToProfile => loc.GetString("ParamWatermark_GoToProfile", "np. Gaming lub default"),
                DeckActionType.OpenFolder => loc.GetString("ParamWatermark_OpenFolder", "np. OBS Studio, Narzędzia, Gry"),
                DeckActionType.ObsSceneSwitch => loc.GetString("ParamWatermark_ObsScene", "np. Główna, Gra, Przerwa, BRB"),
                DeckActionType.TwitchSendMessage => loc.GetString("ParamWatermark_TwitchMessage", "np. GG WP!, !discord, Witajcie na streamie!"),
                DeckActionType.TwitchCreateMarker => loc.GetString("ParamWatermark_TwitchMarker", "np. Epicki clutch, Śmieszna sytuacja, Boss pokonany..."),
                DeckActionType.TwitchStartRaid => loc.GetString("ParamWatermark_TwitchRaid", "np. streamer_pl, shroud, ninja"),
                DeckActionType.TwitchRunCommercial => loc.GetString("ParamWatermark_TwitchCommercial", "np. 30, 60, 90, 180"),
                DeckActionType.KickSendMessage => loc.GetString("ParamWatermark_KickMessage", "np. Siema czat!, !sociale, GG!"),
                DeckActionType.YouTubeLiveViewers => loc.GetString("ParamWatermark_YouTubeViewers", "np. @KanałYouTube, UC..., dQw4w9WgXcQ lub puste dla domyślnego"),
                DeckActionType.StreamlabsSceneSwitch => loc.GetString("ParamWatermark_StreamlabsScene", "np. Starting Soon, In Game, Be Right Back"),
                DeckActionType.DiscordJoinVoiceChannel => loc.GetString("ParamWatermark_DiscordVoice", "np. https://discord.com/channels/... lub ID kanału"),
                DeckActionType.SpotifyAddToQueue => loc.GetString("ParamWatermark_SpotifyQueue", "np. spotify:track:4cOdK2wGLETKBW3PvgPWqT lub link open.spotify.com"),
                DeckActionType.SpotifyPlayUri => loc.GetString("ParamWatermark_SpotifyPlayUri", "np. spotify:playlist:37i9dQZF1DXcBWIGoYBM5M lub link open.spotify.com"),
                DeckActionType.VoicemodVoiceSelect => loc.GetString("ParamWatermark_VoicemodVoice", "np. clean, robot, magic-chords, astronaut"),
                DeckActionType.VoicemodPlaySound => loc.GetString("ParamWatermark_VoicemodSound", "np. Airhorn, Bruh lub wybierz z listy w Ustawieniach..."),
                DeckActionType.HomeAssistantToggle => loc.GetString("ParamWatermark_HomeAssistantToggle", "np. light.salon, switch.gniazdko"),
                DeckActionType.HomeAssistantTurnOn => loc.GetString("ParamWatermark_HomeAssistantTurnOn", "np. light.biurko"),
                DeckActionType.HomeAssistantTurnOff => loc.GetString("ParamWatermark_HomeAssistantTurnOff", "np. light.wszystkie"),
                DeckActionType.HomeAssistantCallService => loc.GetString("ParamWatermark_HomeAssistantCallService", "np. light.turn_on, climate.set_temperature"),
                DeckActionType.ConditionalAction => loc.GetString("ParamWatermark_ConditionalAction", "np. obs64?ObsStopStream:ObsStartStream"),
                _ => loc.GetString("ParamWatermark_Default", "Wartość parametru...")
            };
        }
    }

    public void NotifyLanguageChanged()
    {
        OnLanguageChanged(LocalizationService.Instance.CurrentLanguageCode);
    }

    public string StatusText => IsPressed
        ? LocalizationService.Instance.GetString("Key_Pressed", "Wciśnięty")
        : LocalizationService.Instance.GetString("Key_Released", "Zwolniony");

    public void ResetStyling()
    {
        _backgroundColorHex = "#000000";
        _textColorHex = "#FFFFFF";
        _subtitleColorHex = "#A1A1AA";
        _activeBackgroundColorHex = "";
        _activeTextColorHex = "";
        _activeSubtitleColorHex = "";
        _imagePath = "";
        _activeImagePath = "";
        _imageFitMode = "Cover";
        _imageScale = 100;
        _imageOffsetX = 0;
        _imageOffsetY = 0;
        _activeImageFitMode = "Cover";
        _activeImageScale = 100;
        _activeImageOffsetX = 0;
        _activeImageOffsetY = 0;
        _isActiveToggleState = false;
        _customTitle = "";
        _customSubtitle = "";
        _activeCustomTitle = "";
        _activeCustomSubtitle = "";
        _selectedEditState = 0;
        _fontFamily = "Segoe UI";
        _fontSize = 0;
        _isBold = true;
        _isItalic = false;
        _isStrikeThrough = false;
        _pressMode = KeyPressMode.OnPress;
        _stateMode = KeyStateMode.Static;
        _obsSceneName = "";
        _liveValueText = null;
        _mediaValidationMessage = null;
        _streamDeckPluginUuid = "";
        _streamDeckActionUuid = "";
        _streamDeckSettingsJson = "";
        _pluginDynamicImageBytes = null;
        IsStreamDeckAlert = false;
        IsStreamDeckOk = false;

        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
        _cachedActiveBitmap?.Dispose();
        _cachedActiveBitmap = null;
        SetRenderedJpeg(null);

        OnPropertyChanged(nameof(BackgroundColorHex));
        OnPropertyChanged(nameof(BackgroundColorBrush));
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColorHex));
        OnPropertyChanged(nameof(TextColorBrush));
        OnPropertyChanged(nameof(TitleColorHex));
        OnPropertyChanged(nameof(TitleColorBrush));
        OnPropertyChanged(nameof(TitleColor));
        OnPropertyChanged(nameof(SubtitleColorHex));
        OnPropertyChanged(nameof(SubtitleColorBrush));
        OnPropertyChanged(nameof(ActiveBackgroundColorHex));
        OnPropertyChanged(nameof(ActiveTextColorHex));
        OnPropertyChanged(nameof(ActiveSubtitleColorHex));
        OnPropertyChanged(nameof(StreamDeckPluginUuid));
        OnPropertyChanged(nameof(StreamDeckActionUuid));
        OnPropertyChanged(nameof(StreamDeckSettingsJson));
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(ActiveImagePath));
        OnPropertyChanged(nameof(HasActiveImage));
        OnPropertyChanged(nameof(HasCustomImage));
        OnPropertyChanged(nameof(HasAnyImage));
        OnPropertyChanged(nameof(CustomImageBitmap));
        OnPropertyChanged(nameof(ActiveImageBitmap));
        OnPropertyChanged(nameof(ImageFitMode));
        OnPropertyChanged(nameof(ImageScale));
        OnPropertyChanged(nameof(ImageOffsetX));
        OnPropertyChanged(nameof(ImageOffsetY));
        OnPropertyChanged(nameof(ActiveImageFitMode));
        OnPropertyChanged(nameof(ActiveImageScale));
        OnPropertyChanged(nameof(ActiveImageOffsetX));
        OnPropertyChanged(nameof(ActiveImageOffsetY));
        OnPropertyChanged(nameof(CustomTitle));
        OnPropertyChanged(nameof(CustomSubtitle));
        OnPropertyChanged(nameof(ActiveCustomTitle));
        OnPropertyChanged(nameof(ActiveCustomSubtitle));
        OnPropertyChanged(nameof(FontFamily));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(IsBold));
        OnPropertyChanged(nameof(IsItalic));
        OnPropertyChanged(nameof(IsStrikeThrough));
        OnPropertyChanged(nameof(PressMode));
        OnPropertyChanged(nameof(PressModeDisplay));
        OnPropertyChanged(nameof(SelectedPressModeOption));
        OnPropertyChanged(nameof(StateMode));
        OnPropertyChanged(nameof(StateModeDisplay));
        OnPropertyChanged(nameof(ObsSceneName));
        OnPropertyChanged(nameof(IsEditStateDefault));
        OnPropertyChanged(nameof(IsEditStateActive));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplaySubtitle));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(MediaValidationMessage));
        OnPropertyChanged(nameof(HasMediaValidationMessage));
        NotifyCurrentEditProperties();
    }

    public void LoadAssignment(KeyAssignmentModel? model)
    {
        if (model == null)
        {
            AssignedAction = DeckActionType.None;
            ActionParameter = "";
            ResetStyling();
            return;
        }

        AssignedAction = model.Action;
        PressMode = model.PressMode;
        StateMode = model.StateMode;
        ObsSceneName = !string.IsNullOrWhiteSpace(model.ObsSceneName) 
            ? model.ObsSceneName 
            : (model.Action == DeckActionType.ObsSceneSwitch && !string.IsNullOrWhiteSpace(model.ActionParameter) ? model.ActionParameter : "");
        ActionParameter = !string.IsNullOrWhiteSpace(model.ActionParameter)
            ? model.ActionParameter
            : (model.Action == DeckActionType.ObsSceneSwitch && !string.IsNullOrWhiteSpace(model.ObsSceneName) 
                ? model.ObsSceneName 
                : (model.ApplicationPath ?? model.Url ?? model.TextContent ?? (model.HotkeyKeys != null && model.HotkeyKeys.Count > 0 ? string.Join("+", model.HotkeyKeys) : "")));
        CustomTitle = !string.IsNullOrWhiteSpace(model.CustomTitle) ? model.CustomTitle : (model.LabelText ?? "");
        CustomSubtitle = model.CustomSubtitle ?? "";
        ActiveCustomTitle = model.ActiveCustomTitle ?? "";
        ActiveCustomSubtitle = model.ActiveCustomSubtitle ?? "";
        BackgroundColorHex = string.IsNullOrWhiteSpace(model.BackgroundColorHex) ? "#000000" : model.BackgroundColorHex;
        TextColorHex = !string.IsNullOrWhiteSpace(model.TextColorHex) ? model.TextColorHex : (!string.IsNullOrWhiteSpace(model.LabelColor) ? model.LabelColor : "#FFFFFF");
        SubtitleColorHex = !string.IsNullOrWhiteSpace(model.SubtitleColorHex) ? model.SubtitleColorHex : "#A1A1AA";
        ActiveBackgroundColorHex = model.ActiveBackgroundColorHex ?? "";
        ActiveTextColorHex = model.ActiveTextColorHex ?? "";
        ActiveSubtitleColorHex = model.ActiveSubtitleColorHex ?? "";
        ImagePath = !string.IsNullOrWhiteSpace(model.ImagePath) ? model.ImagePath : (model.CustomIconPath ?? "");
        ActiveImagePath = model.ActiveImagePath ?? "";
        ImageFitMode = !string.IsNullOrWhiteSpace(model.ImageFitMode) ? model.ImageFitMode : "Cover";
        ImageScale = model.ImageScale > 0 ? model.ImageScale : 100;
        ImageOffsetX = model.ImageOffsetX;
        ImageOffsetY = model.ImageOffsetY;
        ActiveImageFitMode = !string.IsNullOrWhiteSpace(model.ActiveImageFitMode) ? model.ActiveImageFitMode : "Cover";
        ActiveImageScale = model.ActiveImageScale > 0 ? model.ActiveImageScale : 100;
        ActiveImageOffsetX = model.ActiveImageOffsetX;
        ActiveImageOffsetY = model.ActiveImageOffsetY;
        IsActiveToggleState = false;
        SelectedEditState = 0;
        FontFamily = string.IsNullOrWhiteSpace(model.FontFamily) ? "Segoe UI" : model.FontFamily;
        FontSize = model.FontSize;
        IsBold = model.IsBold;
        IsItalic = model.IsItalic;
        IsStrikeThrough = model.IsStrikeThrough;
    }
}

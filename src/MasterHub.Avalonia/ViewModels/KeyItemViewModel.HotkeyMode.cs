using System;
using System.Collections.Generic;
using MasterHub.Core.Actions;
using MasterHub.Core.Localization;
using MasterHub.Core.SystemControl;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>An entry of the "send as" list of a Hotkey key.</summary>
public sealed record HotkeyModeOption(HotkeySendMode Mode, string Name)
{
    public override string ToString() => Name;
}

/// <summary>"Send as: Normal / For games" for Hotkey keys (T-10).</summary>
public partial class KeyItemViewModel
{
    private HotkeySendMode _hotkeySendMode;
    private int _hotkeyHoldMs;

    public static IReadOnlyList<HotkeyModeOption> HotkeyModeOptions => new HotkeyModeOption[]
    {
        new(HotkeySendMode.Normal, LocalizationService.Instance.GetString("Hotkey_ModeNormal", "Normal")),
        new(HotkeySendMode.ForGames, LocalizationService.Instance.GetString("Hotkey_ModeGames", "For games (scan codes)"))
    };

    public bool IsHotkeyAction => AssignedAction == DeckActionType.Hotkey;

    public HotkeySendMode HotkeySendMode
    {
        get => _hotkeySendMode;
        set
        {
            if (!SetField(ref _hotkeySendMode, value)) return;
            OnPropertyChanged(nameof(SelectedHotkeyMode));
            OnPropertyChanged(nameof(ShowsHotkeyHold));
            ActionChangedCallback?.Invoke(this);
        }
    }

    public HotkeyModeOption SelectedHotkeyMode
    {
        get => HotkeyModeOptions[(int)_hotkeySendMode];
        set { if (value != null) HotkeySendMode = value.Mode; }
    }

    /// <summary>Hold time of the main key in ms (For games only); 0 = default 30 ms.</summary>
    public int HotkeyHoldMs
    {
        get => _hotkeyHoldMs;
        set
        {
            int clamped = value <= 0 ? 0 : Math.Clamp(value, HotkeySender.MinHoldMs, HotkeySender.MaxHoldMs);
            if (SetField(ref _hotkeyHoldMs, clamped)) ActionChangedCallback?.Invoke(this);
        }
    }

    public bool ShowsHotkeyHold => _hotkeySendMode == HotkeySendMode.ForGames;

    private void ResetHotkeyMode()
    {
        _hotkeySendMode = HotkeySendMode.Normal;
        _hotkeyHoldMs = 0;
    }

    public void LoadHotkeyMode(HotkeySendMode mode, int holdMs)
    {
        _hotkeySendMode = mode;
        _hotkeyHoldMs = holdMs;
        OnPropertyChanged(nameof(HotkeySendMode));
        OnPropertyChanged(nameof(SelectedHotkeyMode));
        OnPropertyChanged(nameof(HotkeyHoldMs));
        OnPropertyChanged(nameof(ShowsHotkeyHold));
    }
}

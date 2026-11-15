using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Actions;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>An entry of the "what does this gesture do" list.</summary>
public sealed record GestureActionOption(DeckActionType Action, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Press / Hold / Double on one key (T-05). The key's own <c>AssignedAction</c> is the <b>Press</b> (tap) binding; the
/// Hold and Double bindings are stored next to it. A key without extra gestures behaves exactly as before.
/// </summary>
public partial class KeyItemViewModel
{
    private DeckActionType _holdAction;
    private string _holdParameter = "";
    private DeckActionType _doubleAction;
    private string _doubleParameter = "";
    private int _gestureTab;

    private static string? s_optionsLanguage;
    private static IReadOnlyList<GestureActionOption>? s_options;

    /// <summary>Everything a gesture can run (localised, "none" first). Rebuilt when the language changes.</summary>
    public static IReadOnlyList<GestureActionOption> GestureActionOptions
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (s_options != null && s_optionsLanguage == loc.CurrentLanguageCode) return s_options;

            var list = new List<GestureActionOption>
            {
                new(DeckActionType.None, loc.GetString("Gesture_NoAction", "— none —"))
            };
            foreach (var action in Enum.GetValues<DeckActionType>())
            {
                if (action is DeckActionType.None or DeckActionType.DefaultNumber or DeckActionType.StreamDeckPluginAction) continue;
                string name = loc.GetString($"Action_{action}", "");
                if (!string.IsNullOrWhiteSpace(name)) list.Add(new GestureActionOption(action, name));
            }

            s_optionsLanguage = loc.CurrentLanguageCode;
            return s_options = list;
        }
    }

    public DeckActionType HoldAction
    {
        get => _holdAction;
        set
        {
            if (!SetField(ref _holdAction, value)) return;
            OnPropertyChanged(nameof(SelectedHoldOption));
            NotifyGesturesChanged();
        }
    }

    public string HoldParameter
    {
        get => _holdParameter;
        set { if (SetField(ref _holdParameter, value ?? "")) ActionChangedCallback?.Invoke(this); }
    }

    public DeckActionType DoubleAction
    {
        get => _doubleAction;
        set
        {
            if (!SetField(ref _doubleAction, value)) return;
            OnPropertyChanged(nameof(SelectedDoubleOption));
            NotifyGesturesChanged();
        }
    }

    public string DoubleParameter
    {
        get => _doubleParameter;
        set { if (SetField(ref _doubleParameter, value ?? "")) ActionChangedCallback?.Invoke(this); }
    }

    // ── the slot being edited (Press / Hold / Double) ────────────────────────────────────────
    // The action itself is chosen in the Action library (search + categories), not in a list of ~300 entries here: the
    // properties only show what the slot does now and hold its settings.

    /// <summary>The action of the slot selected in the Action tab.</summary>
    public DeckActionType ActiveSlotAction => GestureTab switch
    {
        1 => HoldAction,
        2 => DoubleAction,
        _ => AssignedAction
    };

    public bool HasActiveSlotAction => ActiveSlotAction != DeckActionType.None;

    /// <summary>What the slot does now, in words (a plugin action shows the key's title).</summary>
    public string ActiveSlotName
    {
        get
        {
            var action = ActiveSlotAction;
            if (action == DeckActionType.None) return LocalizationService.Instance.GetString("Gesture_NoAction", "— none —");
            if (GestureTab == 0) return ActionDisplayName;
            return GestureActionOptions.FirstOrDefault(o => o.Action == action)?.Name ?? action.ToString();
        }
    }

    /// <summary>Only the Press slot can hold a Stream Deck plugin action (it needs the key's plugin settings).</summary>
    public bool SlotAcceptsPluginAction => GestureTab == 0;

    /// <summary>
    /// Puts <paramref name="action"/> into the Hold or Double slot (the Press slot is assigned by the Action library as before).
    /// Returns false when that slot cannot take the action.
    /// </summary>
    public bool AssignGestureAction(int slot, DeckActionType action)
    {
        if (slot is not (1 or 2) || action == DeckActionType.StreamDeckPluginAction) return false;

        string parameter = action == DeckActionType.None ? "" : GetDefaultActionParameter(action);
        if (slot == 1) { HoldAction = action; HoldParameter = parameter; }
        else { DoubleAction = action; DoubleParameter = parameter; }
        OnPropertyChanged(nameof(ActiveSlotAction));
        OnPropertyChanged(nameof(ActiveSlotName));
        OnPropertyChanged(nameof(HasActiveSlotAction));
        return true;
    }

    /// <summary>Empties the slot selected in the Action tab (Press clears the whole key like the bin button).</summary>
    [RelayCommand]
    private void ClearActiveSlot()
    {
        if (GestureTab == 0) ClearAction();
        else AssignGestureAction(GestureTab, DeckActionType.None);
    }

    private void NotifyActiveSlotChanged()
    {
        OnPropertyChanged(nameof(ActiveSlotAction));
        OnPropertyChanged(nameof(ActiveSlotName));
        OnPropertyChanged(nameof(HasActiveSlotAction));
        OnPropertyChanged(nameof(SlotAcceptsPluginAction));
    }

    public GestureActionOption SelectedHoldOption
    {
        get => GestureActionOptions.FirstOrDefault(o => o.Action == HoldAction) ?? GestureActionOptions[0];
        set => HoldAction = value?.Action ?? DeckActionType.None;
    }

    public GestureActionOption SelectedDoubleOption
    {
        get => GestureActionOptions.FirstOrDefault(o => o.Action == DoubleAction) ?? GestureActionOptions[0];
        set => DoubleAction = value?.Action ?? DeckActionType.None;
    }

    public bool HasHold => HoldAction != DeckActionType.None;
    public bool HasDouble => DoubleAction != DeckActionType.None;

    /// <summary>True when the key has a Hold and/or Double binding (the engine then has to tell the gestures apart).</summary>
    public bool HasGestures => HasHold || HasDouble;

    /// <summary>Small label for the badge on the key: <c>H</c>, <c>D</c> or <c>H·D</c>.</summary>
    public string GestureBadge => HasHold && HasDouble ? "H·D" : HasHold ? "H" : HasDouble ? "D" : "";

    /// <summary>The selected tab of the gesture editor: 0 = Press (the main action), 1 = Hold, 2 = Double.</summary>
    public int GestureTab
    {
        get => _gestureTab;
        set
        {
            if (!SetField(ref _gestureTab, value)) return;
            OnPropertyChanged(nameof(IsPressTab));
            OnPropertyChanged(nameof(IsHoldTab));
            OnPropertyChanged(nameof(IsDoubleTab));
            NotifyActiveSlotChanged();
        }
    }

    public bool IsPressTab => GestureTab == 0;
    public bool IsHoldTab => GestureTab == 1;
    public bool IsDoubleTab => GestureTab == 2;

    [RelayCommand]
    private void SelectGestureTab(string tab) => GestureTab = int.TryParse(tab, out int n) ? Math.Clamp(n, 0, 2) : 0;

    /// <summary>What the key engine has to know to recognise this key's gestures.</summary>
    public GestureBindings GestureBindings
    {
        get
        {
            // A Hold that shuts the PC down must be held as long as a hardware power key (T-03), unless the key opted out.
            int? holdMs = HasHold && PowerActionGuard.IsDestructive(HoldAction) && !SkipPowerConfirmation
                ? PowerActionGuard.HoldToConfirmMs
                : null;
            // a timer key understands Hold (reset) and Double (+1 min / split) without any set-up: they are its built-in gestures
            return new GestureBindings(HasHold || IsTimerAction, HasDouble || IsTimerAction, holdMs);
        }
    }

    /// <summary>How long the key must be held before its power action runs (T-03 bar on the key), or <c>null</c>.</summary>
    public int? HoldToConfirmMs =>
        HoldConfirmProgress.DurationFor(AssignedAction, SkipPowerConfirmation, HasGestures, GestureBindings.HoldMs);

    private void NotifyGesturesChanged()
    {
        OnPropertyChanged(nameof(HasHold));
        OnPropertyChanged(nameof(HasDouble));
        OnPropertyChanged(nameof(HasGestures));
        OnPropertyChanged(nameof(GestureBadge));
        NotifyActiveSlotChanged();
        ActionChangedCallback?.Invoke(this);
    }

    /// <summary>The model to save, or <c>null</c> when the key has no extra gestures (so old-style keys stay old-style).</summary>
    public KeyGestures? ToGesturesModel()
    {
        if (!HasGestures) return null;
        return new KeyGestures
        {
            Hold = HasHold ? new KeyGestureBinding { Action = HoldAction, ActionParameter = HoldParameter } : null,
            Double = HasDouble ? new KeyGestureBinding { Action = DoubleAction, ActionParameter = DoubleParameter } : null
        };
    }

    public void LoadGestures(KeyGestures? model)
    {
        _holdAction = model?.Hold?.Action ?? DeckActionType.None;
        _holdParameter = model?.Hold?.ActionParameter ?? "";
        _doubleAction = model?.Double?.Action ?? DeckActionType.None;
        _doubleParameter = model?.Double?.ActionParameter ?? "";
        OnPropertyChanged(nameof(HoldAction));
        OnPropertyChanged(nameof(HoldParameter));
        OnPropertyChanged(nameof(DoubleAction));
        OnPropertyChanged(nameof(DoubleParameter));
        OnPropertyChanged(nameof(SelectedHoldOption));
        OnPropertyChanged(nameof(SelectedDoubleOption));
        OnPropertyChanged(nameof(HasHold));
        OnPropertyChanged(nameof(HasDouble));
        OnPropertyChanged(nameof(HasGestures));
        OnPropertyChanged(nameof(GestureBadge));
        NotifyActiveSlotChanged();
    }

    private void ResetGestures()
    {
        _holdAction = DeckActionType.None;
        _holdParameter = "";
        _doubleAction = DeckActionType.None;
        _doubleParameter = "";
        _gestureTab = 0;
    }
}

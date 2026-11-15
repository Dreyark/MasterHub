using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Looks;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>An entry of the condition list of a "When" rule.</summary>
public sealed record LookConditionOption(LookConditionKind Kind, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Edits one "When" rule of a key (the inspector rows bind to it).</summary>
public sealed partial class LookRuleItemViewModel : ObservableObject
{
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$");
    private readonly Action _changed;

    public LookRuleItemViewModel(KeyLookRule rule, Action changed)
    {
        Rule = rule;
        _changed = changed;
        _background = FromHex(rule.BackgroundColorHex, Color.Parse("#B3261E"));   // what the picker starts at
        _textColor = FromHex(rule.TextColorHex, Color.Parse("#FFFFFF"));
    }

    public KeyLookRule Rule { get; }

    public IReadOnlyList<LookConditionOption> Conditions => KeyItemViewModel.LookConditionOptions;

    public bool Enabled
    {
        get => Rule.Enabled;
        set { Rule.Enabled = value; OnPropertyChanged(); _changed(); }
    }

    public LookConditionOption SelectedCondition
    {
        get => Conditions.First(c => c.Kind == Rule.When.Kind);
        set
        {
            Rule.When.Kind = value.Kind;
            OnPropertyChanged(nameof(SelectedCondition));
            OnPropertyChanged(nameof(ShowsThreshold));
            OnPropertyChanged(nameof(ShowsText));
            _changed();
        }
    }

    public bool ShowsThreshold => Rule.When.Kind is >= LookConditionKind.CpuAbove and <= LookConditionKind.GpuTempAbove;
    public bool ShowsText => Rule.When.Kind is LookConditionKind.AppInFront or LookConditionKind.TimeBetween;

    public double Threshold
    {
        get => Rule.When.Threshold;
        set { Rule.When.Threshold = Math.Clamp(value, 0, 200); OnPropertyChanged(); _changed(); }
    }

    public string ConditionText
    {
        get => Rule.When.Text;
        set { Rule.When.Text = value ?? ""; OnPropertyChanged(); _changed(); }
    }

    // colours: a check box says whether the rule changes the colour at all (empty = the key keeps its own), the picker sets it
    private Color _background, _textColor;

    public bool UseBackground
    {
        get => Rule.BackgroundColorHex != null;
        set { Rule.BackgroundColorHex = value ? ToHex(_background) : null; OnPropertyChanged(); _changed(); }
    }

    public Color BackgroundColor
    {
        get => _background;
        set { _background = value; if (UseBackground) Rule.BackgroundColorHex = ToHex(value); OnPropertyChanged(); _changed(); }
    }

    public bool UseTextColor
    {
        get => Rule.TextColorHex != null;
        set { Rule.TextColorHex = value ? ToHex(_textColor) : null; OnPropertyChanged(); _changed(); }
    }

    public Color TextColor
    {
        get => _textColor;
        set { _textColor = value; if (UseTextColor) Rule.TextColorHex = ToHex(value); OnPropertyChanged(); _changed(); }
    }

    private static Color FromHex(string? hex, Color fallback) => hex != null && Hex.IsMatch(hex.Trim()) && Color.TryParse(hex.Trim(), out var c) ? c : fallback;

    internal static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public string Title { get => Rule.Title ?? ""; set { Rule.Title = string.IsNullOrWhiteSpace(value) ? null : value; OnPropertyChanged(); _changed(); } }
    public string Subtitle { get => Rule.Subtitle ?? ""; set { Rule.Subtitle = string.IsNullOrWhiteSpace(value) ? null : value; OnPropertyChanged(); _changed(); } }

}

/// <summary>Conditional looks of a key (T-12): rules that change colours or label while a condition holds, and label tokens.</summary>
public partial class KeyItemViewModel
{
    private KeyLookState _lookState = new();

    /// <summary>The rules saved with the key (empty = a plain key).</summary>
    public List<KeyLookRule> LookRules { get; private set; } = new();

    public ObservableCollection<LookRuleItemViewModel> LookRuleItems { get; } = new();

    /// <summary>What the rules that hold apply right now (not saved).</summary>
    public AppliedLook AppliedLook { get; set; } = AppliedLook.None;

    /// <summary>The picture is only redrawn when this changes (applied rules + the label with its tokens filled).</summary>
    public string LookRenderSignature { get; set; } = "";

    public KeyLookState LookState => _lookState;

    public bool HasLookRules => LookRules.Count > 0;

    public static IReadOnlyList<LookConditionOption> LookConditionOptions
    {
        get
        {
            var loc = LocalizationService.Instance;
            return new LookConditionOption[]
            {
                new(LookConditionKind.None, loc.GetString("Look_None", "— choose a condition —")),
                new(LookConditionKind.CpuAbove, loc.GetString("Look_CpuAbove", "CPU usage at or above (%)")),
                new(LookConditionKind.RamAbove, loc.GetString("Look_RamAbove", "Memory usage at or above (%)")),
                new(LookConditionKind.GpuAbove, loc.GetString("Look_GpuAbove", "GPU usage at or above (%)")),
                new(LookConditionKind.CpuTempAbove, loc.GetString("Look_CpuTempAbove", "CPU temperature at or above (°C)")),
                new(LookConditionKind.GpuTempAbove, loc.GetString("Look_GpuTempAbove", "GPU temperature at or above (°C)")),
                new(LookConditionKind.MusicPlaying, loc.GetString("Look_MusicPlaying", "Music is playing")),
                new(LookConditionKind.MicMuted, loc.GetString("Look_MicMuted", "Microphone is muted")),
                new(LookConditionKind.MasterMuted, loc.GetString("Look_MasterMuted", "Speakers are muted")),
                new(LookConditionKind.AppInFront, loc.GetString("Look_AppInFront", "A program is in front (name)")),
                new(LookConditionKind.TimeBetween, loc.GetString("Look_TimeBetween", "Time of day (HH:MM-HH:MM)")),
                new(LookConditionKind.StatusWaiting, loc.GetString("Look_StatusWaiting", "An app waits for you (e.g. Claude Code asks)")),
                new(LookConditionKind.StatusWorking, loc.GetString("Look_StatusWorking", "An app is working")),
                new(LookConditionKind.StatusDone, loc.GetString("Look_StatusDone", "An app is done")),
                new(LookConditionKind.StatusError, loc.GetString("Look_StatusError", "An app reports an error"))
            };
        }
    }

    [RelayCommand]
    private void AddLookRule()
    {
        var rule = new KeyLookRule { When = new KeyLookCondition { Kind = LookConditionKind.None } };
        LookRules.Add(rule);
        LookRuleItems.Add(new LookRuleItemViewModel(rule, NotifyLooksChanged));
        NotifyLooksChanged();
    }

    [RelayCommand]
    private void RemoveLookRule(LookRuleItemViewModel? item)
    {
        if (item == null) return;
        LookRules.Remove(item.Rule);
        LookRuleItems.Remove(item);
        NotifyLooksChanged();
    }

    internal void NotifyLooksChanged()
    {
        _lookState.Reset();
        OnPropertyChanged(nameof(HasLookRules));
        ActionChangedCallback?.Invoke(this);
    }

    /// <summary>The rules to save, or <c>null</c> when the key has none (an old-style key stays old-style).</summary>
    public List<KeyLookRule>? ToLookModel() => LookRules.Count == 0 ? null : LookRules;

    public void LoadLookRules(List<KeyLookRule>? rules)
    {
        LookRules = rules ?? new List<KeyLookRule>();
        LookRuleItems.Clear();
        foreach (var rule in LookRules) LookRuleItems.Add(new LookRuleItemViewModel(rule, NotifyLooksChanged));
        _lookState = new KeyLookState();
        AppliedLook = AppliedLook.None;
        LookRenderSignature = "";
        OnPropertyChanged(nameof(HasLookRules));
    }

    private void ResetLooks() => LoadLookRules(null);

    /// <summary>The text colour the key is drawn with (a rule that holds may change it).</summary>
    public string RenderTextColorHex => AppliedLook.Text ?? TextColorHex;

    public string RenderSubtitleColorHex => AppliedLook.SubtitleColor ?? SubtitleColorHex;
}

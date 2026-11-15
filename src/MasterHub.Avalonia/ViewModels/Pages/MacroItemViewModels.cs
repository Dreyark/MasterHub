using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Core.Macros;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>Pozycja listy z etykietą (ComboBox: tryby odtwarzania, opóźnień, wyzwalacze).</summary>
public sealed record OptionItem<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Wyzwalacz do wyboru w UI (przycisk myszy, przechył kółka, klawisz).</summary>
public sealed record TriggerOption(MacroTriggerKind Kind, int Code, string Label)
{
    public override string ToString() => Label;
}

public partial class MacroListItemViewModel : ObservableObject
{
    public string Id { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private int _stepCount;
    [ObservableProperty] private bool _isRunning;

    public MacroListItemViewModel(MacroDefinition macro)
    {
        Id = macro.Id;
        _name = macro.Name;
        _stepCount = macro.Steps.Count;
    }
}

/// <summary>Krok makra w edytorze (opis, edycja opóźnienia / tekstu).</summary>
public partial class MacroStepItemViewModel : ObservableObject
{
    public MacroStep Step { get; }

    public MacroStepItemViewModel(MacroStep step) => Step = step;

    public string Description => VirtualKeyNames.Describe(Step);
    public bool IsDelay => Step.Type == MacroStepType.Delay;
    public bool IsText => Step.Type == MacroStepType.Text;
    public bool IsPlain => !IsDelay && !IsText;

    public decimal DelayMs
    {
        get => Step.DelayMs;
        set
        {
            Step.DelayMs = (int)System.Math.Clamp(value, 0, 600_000);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Description));
        }
    }

    public string TextValue
    {
        get => Step.Text ?? "";
        set
        {
            Step.Text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Description));
        }
    }
}

public sealed class MacroBindingItemViewModel
{
    public MacroBinding Binding { get; }
    public string Label { get; }
    public string Scope { get; }

    public MacroBindingItemViewModel(MacroBinding binding, string label, string scope)
    {
        Binding = binding;
        Label = label;
        Scope = scope;
    }
}

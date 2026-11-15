using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Macros;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>Nagrywanie oraz ręczna edycja kroków makra.</summary>
public partial class MacrosPageViewModel
{
    public ObservableCollection<OptionItem<MacroDelayMode>> DelayModes { get; } = new();

    [ObservableProperty] private OptionItem<MacroDelayMode>? _selectedDelayMode;
    [ObservableProperty] private decimal _fixedDelayMs = 50;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _recordingStatus = "";
    [ObservableProperty] private decimal _newDelayMs = 100;
    [ObservableProperty] private string _newText = "";

    public bool IsNotRecording => !IsRecording;
    public bool IsFixedDelayMode => SelectedDelayMode?.Value == MacroDelayMode.Fixed;

    private void BuildRecordingOptions()
    {
        var current = SelectedDelayMode?.Value ?? MacroDelayMode.Recorded;
        DelayModes.Clear();
        DelayModes.Add(new(MacroDelayMode.Recorded, L("Macro_DelayRecorded", "Rzeczywiste opóźnienia")));
        DelayModes.Add(new(MacroDelayMode.Fixed, L("Macro_DelayFixed", "Stałe opóźnienie")));
        DelayModes.Add(new(MacroDelayMode.None, L("Macro_DelayNone", "Bez opóźnień")));
        SelectedDelayMode = DelayModes.First(m => m.Value == current);
    }

    partial void OnSelectedDelayModeChanged(OptionItem<MacroDelayMode>? value) => OnPropertyChanged(nameof(IsFixedDelayMode));

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(IsNotRecording));

    [RelayCommand]
    public void StartRecording()
    {
        if (IsRecording) return;
        if (SelectedItem == null) NewMacro();
        _service.StartRecording(SelectedDelayMode?.Value ?? MacroDelayMode.Recorded, (int)FixedDelayMs);
    }

    [RelayCommand]
    public void StopRecording()
    {
        if (!IsRecording) return;
        // Kliknięcie tego przycisku jest ostatnim zdarzeniem w nagraniu - odcinamy je (trimTrailingStopClick).
        var recorded = _service.StopRecording(trimTrailingStopClick: true);
        foreach (var s in recorded) Steps.Add(new MacroStepItemViewModel(s));
        StatusMessage = string.Format(L("Macro_RecordedSteps", "Nagrano kroków: {0}. Kliknij Zapisz, aby zachować makro."), recorded.Count);
    }

    private void OnRecordingStateChanged(bool recording) => Dispatcher.UIThread.Post(() =>
    {
        IsRecording = recording;
        RecordingStatus = recording ? string.Format(L("Macro_Recording", "Nagrywanie… {0} zdarzeń"), 0) : "";
    });

    private void OnRecordedStepCountChanged(int count) => Dispatcher.UIThread.Post(() =>
    {
        if (IsRecording) RecordingStatus = string.Format(L("Macro_Recording", "Nagrywanie… {0} zdarzeń"), count);
    });

    [RelayCommand]
    public void AddDelayStep() => Steps.Add(new MacroStepItemViewModel(MacroStep.Delay((int)Math.Clamp(NewDelayMs, 0, 600_000))));

    [RelayCommand]
    public void AddTextStep()
    {
        if (string.IsNullOrEmpty(NewText)) return;
        Steps.Add(new MacroStepItemViewModel(MacroStep.TypeText(NewText)));
        NewText = "";
    }

    [RelayCommand]
    public void ClearSteps() => Steps.Clear();

    [RelayCommand]
    public void RemoveStep(MacroStepItemViewModel? step)
    {
        if (step != null) Steps.Remove(step);
    }

    [RelayCommand]
    public void MoveStepUp(MacroStepItemViewModel? step)
    {
        int i = step == null ? -1 : Steps.IndexOf(step);
        if (i > 0) Steps.Move(i, i - 1);
    }

    [RelayCommand]
    public void MoveStepDown(MacroStepItemViewModel? step)
    {
        int i = step == null ? -1 : Steps.IndexOf(step);
        if (i >= 0 && i < Steps.Count - 1) Steps.Move(i, i + 1);
    }
}

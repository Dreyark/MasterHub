using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Macros;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>
/// Strona "Makra": lista makr, edytor kroków, nagrywanie (jak w Razer Synapse) oraz przypisania do przycisków myszy.
/// Logika wyboru/zapisu/nagrywania jest tutaj; przypisania wyzwalaczy w MacrosPageViewModel.Bindings.cs.
/// </summary>
public partial class MacrosPageViewModel : ObservableObject, IDisposable
{
    private readonly IMacroService _service;
    private bool _loadingEditor;

    public ObservableCollection<MacroListItemViewModel> Items { get; } = new();
    public ObservableCollection<MacroStepItemViewModel> Steps { get; } = new();

    [ObservableProperty] private MacroListItemViewModel? _selectedItem;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private OptionItem<MacroPlaybackMode>? _selectedMode;
    [ObservableProperty] private decimal _editRepeatCount = 1;
    [ObservableProperty] private decimal _editRepeatDelayMs;
    [ObservableProperty] private string _statusMessage = "";

    public bool HasSelection => SelectedItem != null;
    public bool HasNoItems => Items.Count == 0;
    public bool IsRepeatCountMode => SelectedMode?.Value == MacroPlaybackMode.RepeatCount;
    public bool UsesRepeatDelay => SelectedMode?.Value != MacroPlaybackMode.Once;

    public ObservableCollection<OptionItem<MacroPlaybackMode>> Modes { get; } = new();

    public MacrosPageViewModel(IMacroService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        BuildOptionLists();
        ReloadList(selectId: null);

        _service.LibraryChanged += OnLibraryChanged;
        _service.MacroRunningChanged += OnMacroRunningChanged;
        _service.RecordingStateChanged += OnRecordingStateChanged;
        _service.RecordedStepCountChanged += OnRecordedStepCountChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private static string L(string key, string fallback) => LocalizationService.Instance.GetString(key, fallback);

    private void BuildOptionLists()
    {
        var current = SelectedMode?.Value ?? MacroPlaybackMode.Once;
        Modes.Clear();
        Modes.Add(new(MacroPlaybackMode.Once, L("Macro_ModeOnce", "Raz po naciśnięciu")));
        Modes.Add(new(MacroPlaybackMode.WhilePressed, L("Macro_ModeWhilePressed", "Powtarzaj podczas przytrzymania")));
        Modes.Add(new(MacroPlaybackMode.Toggle, L("Macro_ModeToggle", "Przełącznik (start / stop)")));
        Modes.Add(new(MacroPlaybackMode.RepeatCount, L("Macro_ModeRepeat", "Powtórz N razy")));
        SelectedMode = Modes.First(m => m.Value == current);

        BuildRecordingOptions();
        BuildTriggerOptions();
    }

    private void OnLanguageChanged(string _) => Dispatcher.UIThread.Post(() =>
    {
        _loadingEditor = true;
        try { BuildOptionLists(); RefreshBindings(); }
        finally { _loadingEditor = false; }
    });

    partial void OnSelectedItemChanged(MacroListItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        LoadEditor(value?.Id);
    }

    partial void OnSelectedModeChanged(OptionItem<MacroPlaybackMode>? value)
    {
        OnPropertyChanged(nameof(IsRepeatCountMode));
        OnPropertyChanged(nameof(UsesRepeatDelay));
    }

    private void ReloadList(string? selectId)
    {
        var macros = _service.Macros;
        Items.Clear();
        foreach (var m in macros)
        {
            Items.Add(new MacroListItemViewModel(m) { IsRunning = _service.IsRunning(m.Id) });
        }
        OnPropertyChanged(nameof(HasNoItems));
        SelectedItem = Items.FirstOrDefault(i => i.Id == selectId) ?? Items.FirstOrDefault();
        if (SelectedItem == null) LoadEditor(null);
    }

    private void LoadEditor(string? id)
    {
        _loadingEditor = true;
        try
        {
            Steps.Clear();
            var macro = id == null ? null : _service.GetMacro(id);
            if (macro == null)
            {
                EditName = "";
                EditRepeatCount = 1;
                EditRepeatDelayMs = 0;
                RefreshBindings();
                return;
            }

            EditName = macro.Name;
            SelectedMode = Modes.First(m => m.Value == macro.Mode);
            EditRepeatCount = macro.RepeatCount;
            EditRepeatDelayMs = macro.RepeatDelayMs;
            foreach (var s in macro.Steps) Steps.Add(new MacroStepItemViewModel(s));
            RefreshBindings();
        }
        finally
        {
            _loadingEditor = false;
        }
    }

    private MacroDefinition BuildDefinition()
    {
        var existing = SelectedItem == null ? null : _service.GetMacro(SelectedItem.Id);
        var def = existing ?? new MacroDefinition();
        def.Name = string.IsNullOrWhiteSpace(EditName) ? L("Macro_DefaultName", "Nowe makro") : EditName.Trim();
        def.Mode = SelectedMode?.Value ?? MacroPlaybackMode.Once;
        def.RepeatCount = (int)Math.Clamp(EditRepeatCount, 1, MacroEngine.MaxRepeatCount);
        def.RepeatDelayMs = (int)Math.Clamp(EditRepeatDelayMs, 0, 600_000);
        def.Steps = Steps.Select(s => s.Step.Clone()).ToList();
        return def;
    }

    // ---------------------------------------------------------------- komendy listy / zapisu

    [RelayCommand]
    public void NewMacro()
    {
        var def = new MacroDefinition { Name = $"{L("Macro_DefaultName", "Nowe makro")} {Items.Count + 1}" };
        _service.SaveMacro(def);
        ReloadList(def.Id);
    }

    [RelayCommand]
    public void SaveMacro()
    {
        if (SelectedItem == null) return;
        var def = BuildDefinition();
        _service.SaveMacro(def);
        ReloadList(def.Id);
        StatusMessage = L("Macro_Saved", "Zapisano makro.");
    }

    [RelayCommand]
    public void DeleteMacro()
    {
        if (SelectedItem == null) return;
        _service.DeleteMacro(SelectedItem.Id);
        ReloadList(null);
    }

    [RelayCommand]
    public void TestMacro()
    {
        if (SelectedItem == null) return;
        // testujemy aktualny stan edytora (także niezapisany)
        var def = BuildDefinition();
        _service.SaveMacro(def);
        _service.Play(def.Id);
    }

    [RelayCommand]
    public void StopMacros() => _service.StopAll();

    private void OnLibraryChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_loadingEditor) return;
        var id = SelectedItem?.Id;
        // odświeżamy tylko listę (nazwy / liczbę kroków), nie nadpisując edytowanych pól
        foreach (var item in Items)
        {
            var m = _service.GetMacro(item.Id);
            if (m != null) { item.Name = m.Name; item.StepCount = m.Steps.Count; }
        }
        if (Items.Count != _service.Macros.Count) ReloadList(id);
        RefreshBindings();
    });

    private void OnMacroRunningChanged(string id, bool running) => Dispatcher.UIThread.Post(() =>
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item != null) item.IsRunning = running;
    });

    public void Dispose()
    {
        _service.LibraryChanged -= OnLibraryChanged;
        _service.MacroRunningChanged -= OnMacroRunningChanged;
        _service.RecordingStateChanged -= OnRecordingStateChanged;
        _service.RecordedStepCountChanged -= OnRecordedStepCountChanged;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
}

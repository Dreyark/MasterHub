using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Macros;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>Przypisania makra do przycisków myszy / klawiszy.</summary>
public partial class MacrosPageViewModel
{
    public ObservableCollection<TriggerOption> TriggerOptions { get; } = new();
    public ObservableCollection<MacroBindingItemViewModel> Bindings { get; } = new();

    [ObservableProperty] private TriggerOption? _selectedTrigger;
    [ObservableProperty] private bool _onlyRazerMouse = true;
    [ObservableProperty] private int _capturedKeyCode;
    [ObservableProperty] private string _capturedKeyName = "";

    public bool IsKeyTrigger => SelectedTrigger?.Kind == MacroTriggerKind.Key;
    public bool IsMouseTrigger => SelectedTrigger != null && SelectedTrigger.Kind != MacroTriggerKind.Key;
    public bool HasNoBindings => Bindings.Count == 0;

    private void BuildTriggerOptions()
    {
        var current = SelectedTrigger;
        TriggerOptions.Clear();
        TriggerOptions.Add(new(MacroTriggerKind.MouseButton, (int)MacroMouseButton.X1, L("Macro_TriggerX1", "Przycisk boczny 1 (wstecz)")));
        TriggerOptions.Add(new(MacroTriggerKind.MouseButton, (int)MacroMouseButton.X2, L("Macro_TriggerX2", "Przycisk boczny 2 (dalej)")));
        TriggerOptions.Add(new(MacroTriggerKind.MouseButton, (int)MacroMouseButton.Middle, L("Macro_TriggerMiddle", "Środkowy przycisk (kółko)")));
        TriggerOptions.Add(new(MacroTriggerKind.WheelTiltLeft, 0, L("Macro_TiltLeft", "Przechył kółka w lewo")));
        TriggerOptions.Add(new(MacroTriggerKind.WheelTiltRight, 0, L("Macro_TiltRight", "Przechył kółka w prawo")));
        TriggerOptions.Add(new(MacroTriggerKind.Key, 0, L("Macro_TriggerKey", "Klawisz klawiatury (np. F13-F24)")));
        SelectedTrigger = current == null ? TriggerOptions[0]
            : TriggerOptions.FirstOrDefault(t => t.Kind == current.Kind && t.Code == current.Code) ?? TriggerOptions[0];
    }

    partial void OnSelectedTriggerChanged(TriggerOption? value)
    {
        OnPropertyChanged(nameof(IsKeyTrigger));
        OnPropertyChanged(nameof(IsMouseTrigger));
    }

    private string DescribeBinding(MacroBinding b) => b.TriggerKind switch
    {
        MacroTriggerKind.Key => $"{L("Macro_TriggerKey", "Klawisz")}: {VirtualKeyNames.Get(b.Code)}",
        MacroTriggerKind.WheelTiltLeft => L("Macro_TiltLeft", "Przechył kółka w lewo"),
        MacroTriggerKind.WheelTiltRight => L("Macro_TiltRight", "Przechył kółka w prawo"),
        _ => TriggerOptions.FirstOrDefault(t => t.Kind == MacroTriggerKind.MouseButton && t.Code == b.Code)?.Label ?? ((MacroMouseButton)b.Code).ToString()
    };

    private void RefreshBindings()
    {
        Bindings.Clear();
        if (SelectedItem != null)
        {
            foreach (var b in _service.Bindings.Where(b => b.MacroId == SelectedItem.Id))
            {
                bool mouse = b.TriggerKind != MacroTriggerKind.Key;
                string scope = !mouse ? "" : b.OnlyRazerMouse ? L("Macro_OnlyRazerShort", "tylko mysz Razer") : L("Macro_AnyMouse", "dowolna mysz");
                Bindings.Add(new MacroBindingItemViewModel(b, DescribeBinding(b), scope));
            }
        }
        OnPropertyChanged(nameof(HasNoBindings));
    }

    /// <summary>Wywoływane z widoku po naciśnięciu klawisza w polu "wciśnij klawisz" (kod VK).</summary>
    public void CaptureKey(int vk)
    {
        CapturedKeyCode = vk;
        CapturedKeyName = VirtualKeyNames.Get(vk);
    }

    [RelayCommand]
    public void AddBinding()
    {
        if (SelectedItem == null || SelectedTrigger == null) return;
        int code = SelectedTrigger.Kind == MacroTriggerKind.Key ? CapturedKeyCode : SelectedTrigger.Code;
        if (SelectedTrigger.Kind == MacroTriggerKind.Key && code == 0)
        {
            StatusMessage = L("Macro_PressKey", "Kliknij pole i naciśnij klawisz…");
            return;
        }

        // makro musi być zapisane, zanim dostanie przypisanie (przypisanie wskazuje jego Id)
        _service.SaveMacro(BuildDefinition());
        _service.SaveBinding(new MacroBinding
        {
            TriggerKind = SelectedTrigger.Kind,
            Code = code,
            MacroId = SelectedItem.Id,
            OnlyRazerMouse = OnlyRazerMouse
        });
        StatusMessage = L("Macro_BindingAdded", "Dodano przypisanie.");
    }

    [RelayCommand]
    public void RemoveBinding(MacroBindingItemViewModel? item)
    {
        if (item != null) _service.DeleteBinding(item.Binding.Id);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MasterHub.Core.Actions;
using MasterHub.Core.Macros;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>Akcja "Uruchom makro": wybór makra z biblioteki (ActionParameter = Id makra).</summary>
public partial class KeyItemViewModel
{
    private static IMacroService? MacroSvc()
    {
        try { return App.Services?.GetService<IMacroService>(); }
        catch (ObjectDisposedException) { return null; }
    }

    public bool IsMacroAction => AssignedAction == DeckActionType.MacroPlay;

    public IReadOnlyList<MacroDefinition> AvailableMacros => MacroSvc()?.Macros ?? Array.Empty<MacroDefinition>();

    public MacroDefinition? SelectedMacro
    {
        get => string.IsNullOrWhiteSpace(ActionParameter) ? null : AvailableMacros.FirstOrDefault(m => m.Id == ActionParameter);
        set
        {
            if (value == null) return;
            _actionParameter = value.Id;
            OnPropertyChanged(nameof(ActionParameter));
            OnPropertyChanged();
            NotifyMacroChanged();
            ActionChangedCallback?.Invoke(this);
        }
    }

    public bool IsMacroRunning => IsMacroAction && !string.IsNullOrWhiteSpace(ActionParameter) &&
                                  (MacroSvc()?.IsRunning(ActionParameter) ?? false);

    private string MacroDisplayTitle()
    {
        string? name = SelectedMacro?.Name;
        if (string.IsNullOrWhiteSpace(name)) return "MACRO";
        return name.Length > 14 ? name[..14] : name;
    }

    /// <summary>Odświeża tytuł/ikonę/stan klawisza po zmianie biblioteki makr lub stanu odtwarzania.</summary>
    public void NotifyMacroChanged()
    {
        OnPropertyChanged(nameof(ActionDisplayName));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplaySubtitle));
        OnPropertyChanged(nameof(ActionIcon));
        OnPropertyChanged(nameof(AvailableMacros));
        OnPropertyChanged(nameof(IsMacroRunning));
    }

    [RelayCommand]
    public void OpenMacrosPage()
    {
        GetService<MasterHub.Avalonia.Services.INavigationService>()
            ?.NavigateTo(MasterHub.Avalonia.Services.NavigationPage.Macros);
    }
}

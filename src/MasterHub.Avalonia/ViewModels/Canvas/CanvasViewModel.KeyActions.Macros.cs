using System;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MasterHub.Core.Actions;
using MasterHub.Core.Macros;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>Akcja decka "Uruchom makro" oraz odświeżanie klawiszy makr (nazwa, stan "uruchomione").</summary>
public partial class CanvasViewModel
{
    private IMacroService? _macroService;

    private IMacroService? MacroSvc
    {
        get
        {
            if (_macroService != null) return _macroService;
            try { _macroService = App.Services?.GetService<IMacroService>(); }
            catch (ObjectDisposedException) { }
            return _macroService;
        }
    }

    private bool TryExecuteMacroAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            case DeckActionType.MacroPlay:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter)) MacroSvc?.Play(key.ActionParameter);
                break;
            default:
                return false;
        }

        return true;
    }

    private void WireMacroEvents()
    {
        var svc = MacroSvc;
        if (svc == null) return;
        svc.MacroRunningChanged += OnMacroRunningChanged;
        svc.LibraryChanged += OnMacroLibraryChanged;
    }

    private void UnwireMacroEvents()
    {
        var svc = _macroService;
        if (svc == null) return;
        svc.MacroRunningChanged -= OnMacroRunningChanged;
        svc.LibraryChanged -= OnMacroLibraryChanged;
    }

    private void OnMacroRunningChanged(string macroId, bool running) => RefreshMacroKeys(macroId);

    private void OnMacroLibraryChanged() => RefreshMacroKeys(null);

    private void RefreshMacroKeys(string? macroId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction != DeckActionType.MacroPlay) continue;
                if (macroId != null && key.ActionParameter != macroId) continue;
                key.NotifyMacroChanged();
                RenderKeyVisual(key, sendToHardware: true);
            }
        });
    }
}

using Avalonia.Input;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia;

/// <summary>
/// Studio-wide editing shortcuts (T-06): Ctrl+C / X / V, Ctrl+Shift+V (paste look), Delete, Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z.
/// They only apply on the Studio page and never while a text box has the keyboard (it handles those keys itself first).
/// </summary>
public partial class MainWindow
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not MainViewModel vm || !vm.Navigation.IsStudioSelected) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var canvas = vm.Canvas;

        switch (e.Key)
        {
            case Key.C when ctrl && !shift: canvas.CopyKey(); break;
            case Key.X when ctrl && !shift: canvas.CutKey(); break;
            case Key.V when ctrl && shift: canvas.PasteLook(); break;
            case Key.V when ctrl: canvas.PasteKey(); break;
            case Key.Delete when !ctrl: canvas.ClearKey(); break;
            case Key.Z when ctrl && shift: canvas.RedoEdit(); break;
            case Key.Z when ctrl: canvas.UndoEdit(); break;
            case Key.Y when ctrl: canvas.RedoEdit(); break;
            default: return;
        }

        e.Handled = true;
    }
}

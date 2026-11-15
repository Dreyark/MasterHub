using Avalonia.Controls;
using Avalonia.Input;
using MasterHub.Avalonia.ViewModels.Pages;

namespace MasterHub.Avalonia.Views.Pages;

public partial class MacroBindingsView : UserControl
{
    public MacroBindingsView()
    {
        InitializeComponent();
    }

    /// <summary>Pole "wciśnij klawisz": zamienia klawisz Avalonii na kod VK_* używany przez haki systemowe.</summary>
    private void OnKeyCapture(object? sender, KeyEventArgs e)
    {
        if (DataContext is MacrosPageViewModel vm)
        {
            int vk = MasterHub.Avalonia.Services.KeyCodeMapper.ToVirtualKey(e.Key);
            if (vk > 0) vm.CaptureKey(vk);
        }
        e.Handled = true;
    }
}

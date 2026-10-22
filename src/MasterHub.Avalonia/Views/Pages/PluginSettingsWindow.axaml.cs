using Avalonia.Controls;
using MasterHub.Avalonia.ViewModels.Pages;

namespace MasterHub.Avalonia.Views.Pages;

public partial class PluginSettingsWindow : global::Avalonia.Controls.Window
{
    public PluginSettingsWindow()
    {
        InitializeComponent();
    }

    public PluginSettingsWindow(PluginSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose = Close;
    }
}

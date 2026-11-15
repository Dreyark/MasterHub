using Avalonia.Controls;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Core.Localization;

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
        // Tytuł z formatem {0} nie da się zlokalizować przez {loc:Loc}, więc ustawiamy go tutaj.
        Title = string.Format(
            LocalizationService.Instance.GetString("SD_Win_Title", "Plugin settings: {0}"),
            viewModel.PluginName);
    }
}

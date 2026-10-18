using Avalonia.Controls;
using Avalonia.Threading;
using MasterHub.Avalonia.ViewModels.Pages;

namespace MasterHub.Avalonia.Views.Pages;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
        SettingsPageViewModel.ScrollToVoicemodRequested += OnScrollToVoicemodRequested;
    }

    private void OnScrollToVoicemodRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = this.FindControl<VoicemodSettingsCardView>("VoicemodCard");
            card?.BringIntoView();
        });
    }
}

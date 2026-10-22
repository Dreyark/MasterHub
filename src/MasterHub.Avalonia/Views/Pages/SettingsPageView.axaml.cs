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
        SettingsPageViewModel.ScrollToHomeAssistantRequested += OnScrollToHomeAssistantRequested;
    }

    private void OnScrollToVoicemodRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = this.FindControl<VoicemodSettingsCardView>("VoicemodCard");
            card?.BringIntoView();
        });
    }

    private void OnScrollToHomeAssistantRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = this.FindControl<HomeAssistantSettingsCardView>("HomeAssistantCard");
            card?.BringIntoView();
        });
    }
}

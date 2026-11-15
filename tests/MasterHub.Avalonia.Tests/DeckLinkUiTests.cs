using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Devices;
using MasterHub.Core.Localization;

namespace MasterHub.UiTests;

/// <summary>T-34 #13: the title bar says "Connecting…" while the deck is still being found.</summary>
public class DeckLinkUiTests
{
    [AvaloniaFact]
    public void RightAfterStart_WithNoDeckYet_TheBarSaysConnecting_NotARedDisconnected()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mh_link_" + Guid.NewGuid().ToString("N"));
        try
        {
            var main = StudioShotTests.BuildMain(tmp, NavigationPage.Studio);

            Assert.Equal(DeckLinkPhase.Connecting, main.LinkPhase);
            Assert.Equal(LocalizationService.Instance.GetString("Common_Connecting", "x"), main.ConnectionStatusText);
            Assert.Equal("#FBBF24", main.ConnectionStatusColor);
            Assert.False(main.IsDeviceConnected);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}

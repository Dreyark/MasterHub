using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Controls;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Dialogs;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;

namespace MasterHub.UiTests;

/// <summary>T-02 / T-03 UI: confirmation overlay, backups card and quarantine banner (headless render with real view models).</summary>
public class SafetyUiShotTests
{
    private sealed class PlainProtection : ISecretProtectionService
    {
        public bool IsProtected(string? value) => false;
        public string? Protect(string? plainText) => plainText;
        public string? Unprotect(string? cipherText) => cipherText;
    }

    [AvaloniaFact]
    public void ConfirmOverlay_ShowsRequest_AndButtonsAnswerIt()
    {
        var service = new ConfirmDialogService();
        var vm = new ConfirmOverlayViewModel(service);
        Assert.False(vm.IsVisible);

        var task = service.ConfirmAsync(new ConfirmRequest(
            "Delete profile?", "Profile \"Streaming\" will be deleted. A copy stays in Settings → Backups.", "Delete profile", "Cancel"));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsVisible);
        Assert.Equal("Delete profile", vm.ConfirmLabel);

        var view = new ConfirmOverlayView { DataContext = vm };
        Assert.True(File.Exists(Shot.Render(view, "overlay_confirm", 900, 520)));

        vm.AcceptCommand.Execute(null);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(task.Result);
        Assert.False(vm.IsVisible);
    }

    [AvaloniaFact]
    public void BackupsCard_ListsSnapshots_AndRestoreAsksFirst()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mh_backups_ui_" + Guid.NewGuid().ToString("N"));
        try
        {
            var profiles = new ProfileService(dir, new PlainProtection());
            profiles.Backups.CreateSnapshot("manual");
            profiles.CreateProfile("Streaming", setAsActive: false);
            profiles.Backups.CreateSnapshot("before-delete");

            var confirm = new ConfirmDialogService();
            var vm = new BackupsCardViewModel(profiles, confirm);
            Assert.True(vm.Items.Count >= 2);

            var card = new BackupsCardView { DataContext = vm };
            Assert.True(File.Exists(Shot.Render(card, "card_backups", 900, 700)));

            // Restore does nothing until the user confirms.
            int before = profiles.Backups.List().Count;
            var restore = vm.RestoreCommand.ExecuteAsync(vm.Items.Last());
            Assert.NotNull(confirm.Current);
            Assert.Equal(before, profiles.Backups.List().Count);

            confirm.Cancel();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(restore.IsCompleted); // the continuation ran on the (pumped) UI thread; never block it here
            Assert.Equal(before, profiles.Backups.List().Count); // cancelled: no before-restore snapshot, nothing restored
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void QuarantineBanner_AppearsWhenAFileWasSetAside()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mh_quarantine_ui_" + Guid.NewGuid().ToString("N"));
        try
        {
            var profiles = new ProfileService(dir, new PlainProtection());
            File.WriteAllText(Path.Combine(dir, "profiles", "broken.json"), "{ not json");
            profiles.GetAllProfiles();

            var vm = new QuarantineBannerViewModel(profiles);
            Assert.True(vm.IsVisible);
            Assert.Contains("1", vm.Text);

            Assert.True(File.Exists(Shot.Render(new QuarantineBannerView { DataContext = vm }, "banner_quarantine", 900, 160)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

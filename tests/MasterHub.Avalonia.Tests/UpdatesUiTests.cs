using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;
using MasterHub.Core.Updates;

namespace MasterHub.UiTests;

/// <summary>T-32: the update card – off by default, it only announces and opens a page.</summary>
public sealed class UpdatesUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_updui_" + Guid.NewGuid().ToString("N"));
    private readonly ProfileService _profiles;

    public UpdatesUiTests() => _profiles = new ProfileService(_dir, new SecretProtectionService());

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FakeService : IUpdateService
    {
        public UpdateResult? Last { get; set; }
        public bool Due { get; set; }
        public List<bool> Checks { get; } = new();
        public UpdateResult Next { get; set; } = new(UpdateOutcome.UpToDate, "1.0.0", null, null, null, DateTime.UtcNow);
        public event Action<UpdateResult>? Changed;

        public bool IsCheckDue() => Due;

        public Task<UpdateResult> CheckAsync(bool userAsked, CancellationToken ct = default)
        {
            Checks.Add(userAsked);
            Last = Next;
            Changed?.Invoke(Next);
            return Task.FromResult(Next);
        }
    }

    [AvaloniaFact]
    public void ByDefault_ItIsOffAndAsksForARepository_AndChecksNothing()
    {
        var service = new FakeService();
        var vm = new UpdatesViewModel(service, _profiles, _ => { });

        Assert.False(vm.IsEnabled);
        Assert.Equal("", vm.Repository);
        Assert.False(vm.CanCheck);
        Assert.Equal(MasterHub.Core.Localization.LocalizationService.Instance.GetString("Update_NotConfigured", ""), vm.StatusText);
        vm.Start();
        Assert.Empty(service.Checks);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void TheSwitchAndTheRepository_AreSaved()
    {
        var vm = new UpdatesViewModel(new FakeService(), _profiles, _ => { });

        vm.Repository = " acme/deck ";
        vm.IsEnabled = true;

        var saved = new ProfileService(_dir, new SecretProtectionService()).GetAppSettings().Updates;
        Assert.True(saved.Enabled);
        Assert.Equal("acme/deck", saved.Repository);
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task CheckNow_ShowsTheUpdate_AndTheButtonOpensOnlyThatPage()
    {
        var service = new FakeService
        {
            Next = new(UpdateOutcome.Available, "2.0.0", "https://github.com/acme/deck/releases/tag/v2.0.0", "- faster", null, DateTime.UtcNow)
        };
        var opened = new List<string>();
        var vm = new UpdatesViewModel(service, _profiles, opened.Add) { Repository = "acme/deck" };

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(new[] { true }, service.Checks);   // the user asked
        Assert.True(vm.HasUpdate);
        Assert.Contains("2.0.0", vm.StatusText);
        Assert.Equal("- faster", vm.Notes);
        Assert.True(vm.HasNotes);

        vm.OpenReleaseCommand.Execute(null);
        Assert.Equal(new[] { "https://github.com/acme/deck/releases/tag/v2.0.0" }, opened);
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task AFailedCheck_SaysWhy_AndOffersNoPage()
    {
        var service = new FakeService { Next = new(UpdateOutcome.Failed, null, null, null, "No connection to GitHub.", DateTime.UtcNow) };
        var opened = new List<string>();
        var vm = new UpdatesViewModel(service, _profiles, opened.Add) { Repository = "acme/deck" };

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.False(vm.HasUpdate);
        Assert.Contains("No connection", vm.StatusText);
        vm.OpenReleaseCommand.Execute(null);
        Assert.Empty(opened);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void StartChecksOnlyWhenOneIsDue()
    {
        var service = new FakeService { Due = false };
        var vm = new UpdatesViewModel(service, _profiles, _ => { });
        vm.Start();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(service.Checks);

        service.Due = true;
        vm.Start();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { false }, service.Checks);   // an automatic check, not a user's
        vm.Dispose();
    }

    [AvaloniaFact]
    public void TheCard_Renders_AndShowsTheUpdateBoxOnlyWhenThereIsOne()
    {
        var service = new FakeService
        {
            Next = new(UpdateOutcome.Available, "2.0.0", "https://github.com/acme/deck/releases/tag/v2.0.0", "- faster", null, DateTime.UtcNow)
        };
        var vm = new UpdatesViewModel(service, _profiles, _ => { }) { Repository = "acme/deck" };
        var card = new UpdatesCardView { DataContext = vm };
        var window = new Window { Width = 700, Height = 520, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HasUpdate);
        _ = vm.CheckNowCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.True(vm.HasUpdate);
        window.Close();
        vm.Dispose();
    }
}

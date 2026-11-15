using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Core.Integrations.StreamDeck;

namespace MasterHub.UiTests;

/// <summary>After an install the person is told the truth: runs / cannot run (and why) / started but silent.</summary>
public sealed class PluginInstallFollowUpTests
{
    private static StreamDeckInstalledPlugin Plugin(string uuid = "systems.garcia.countdown") => new() { PluginUuid = uuid, Name = "Countdown", Version = "0.2.0", Manifest = new StreamDeckManifest() };

    [Fact]
    public async Task ARunningPlugin_IsReportedAsInstalledAndStarted()
    {
        var (host, fake) = FakePluginHost.Create();
        var plugin = Plugin();
        fake.Connected.Add(plugin.PluginUuid);

        var result = await PluginInstallFollowUp.RunAsync(host, plugin);

        Assert.True(result.Works);
        Assert.False(result.Removed);
        Assert.Contains("StartPluginAsync", fake.Calls);
    }

    [Fact]
    public async Task APluginForLinux_IsNeverStarted_AndIsRemovedWithTheReason()
    {
        var (host, fake) = FakePluginHost.Create();
        var plugin = Plugin();
        fake.Reports[plugin.PluginUuid] = new PluginCompatReport(PluginVerdict.CannotRun, new[] { new PluginIssueItem(PluginIssue.OtherSystemOnly, "Linux") });

        var result = await PluginInstallFollowUp.RunAsync(host, plugin);

        Assert.False(result.Works);
        Assert.True(result.Removed);
        Assert.Contains("Linux", result.Message);
        Assert.Equal(plugin.PluginUuid, Assert.Single(fake.Uninstalled));
        Assert.DoesNotContain("StartPluginAsync", fake.Calls);
    }

    [Fact]
    public async Task AFixableProblem_KeepsThePlugin_ButDoesNotClaimItRuns()
    {
        var (host, fake) = FakePluginHost.Create();
        var plugin = Plugin("com.node");
        fake.Reports[plugin.PluginUuid] = new PluginCompatReport(PluginVerdict.CannotRun, new[] { new PluginIssueItem(PluginIssue.NodeMissing, "20") });

        var result = await PluginInstallFollowUp.RunAsync(host, plugin);

        Assert.False(result.Works);
        Assert.False(result.Removed);
        Assert.Empty(fake.Uninstalled);
        Assert.Contains("20", result.Message);
    }

    [Fact]
    public async Task AStartedPluginThatNeverAnswers_IsNotReportedAsWorking()
    {
        var (host, fake) = FakePluginHost.Create();
        var plugin = Plugin();   // not in Connected

        var result = await PluginInstallFollowUp.RunAsync(host, plugin);

        Assert.False(result.Works);
        Assert.False(result.Removed);
        Assert.Contains("StartPluginAsync", fake.Calls);
    }
}

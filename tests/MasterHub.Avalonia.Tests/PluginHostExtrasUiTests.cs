using System.Reflection;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Actions;
using MasterHub.Core.Dialogs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;

namespace MasterHub.UiTests;

/// <summary>A plugin host with installed plugins whose answers the test chooses.</summary>
public class FakePluginHost : DispatchProxy
{
    public List<StreamDeckInstalledPlugin> Plugins { get; } = new();
    public Dictionary<string, PluginCompatReport> Reports { get; } = new();
    public List<string> Calls { get; } = new();
    public HashSet<string> Connected { get; } = new();
    public List<string> Uninstalled { get; } = new();

    public static (IStreamDeckPluginHost Host, FakePluginHost Fake) Create()
    {
        var host = DispatchProxy.Create<IStreamDeckPluginHost, FakePluginHost>();
        return (host, (FakePluginHost)(object)host);
    }

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        string name = m?.Name ?? "";
        Calls.Add(name);
        switch (name)
        {
            case "get_InstalledPlugins": return Plugins.AsReadOnly();
            case "CheckCompatibility":
                return Reports.TryGetValue((string)args![0]!, out var r) ? r : new PluginCompatReport(PluginVerdict.Runs, Array.Empty<PluginIssueItem>());
            case "GetCpuUse": return new PluginCpuUse((string)args![0]!, 0, false);
            case "IsPluginConnected": return Connected.Contains((string)args![0]!);
            case "UninstallPluginAsync": Uninstalled.Add((string)args![0]!); return Task.FromResult(true);
        }
        var t = m?.ReturnType;
        if (t == null || t == typeof(void)) return null;
        if (t == typeof(Task)) return Task.CompletedTask;
        if (t == typeof(string)) return "";
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var arg = t.GetGenericArguments()[0];
            return typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(arg).Invoke(null, new[] { arg.IsValueType ? Activator.CreateInstance(arg) : null });
        }
        return t.IsValueType ? Activator.CreateInstance(t) : null;
    }
}

/// <summary>T-31 on the store page: badge + reasons, CPU flag, Node download with consent, uninstall lists keys.</summary>
public sealed class PluginHostExtrasUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_sdx_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static StreamDeckInstalledPlugin Plugin(string uuid, bool node = false) => new()
    {
        PluginUuid = uuid,
        Name = "Plugin " + uuid,
        Version = "1.2",
        IsNodeJs = node,
        Manifest = new StreamDeckManifest { Nodejs = node ? new StreamDeckNodejs { Version = "20" } : null }
    };

    [AvaloniaFact]
    public void ThePluginCard_ShowsTheBadgeAndTheReasons()
    {
        var (host, fake) = FakePluginHost.Create();
        fake.Plugins.Add(Plugin("com.node", node: true));
        fake.Reports["com.node"] = new PluginCompatReport(PluginVerdict.CannotRun, new[] { new PluginIssueItem(PluginIssue.NodeMissing, "20") });

        var page = new ActionStorePageViewModel(streamDeckHost: host);
        var item = Assert.Single(page.StreamDeckPlugins);

        Assert.False(item.CanRun);
        Assert.True(item.NeedsNode);
        Assert.Contains("20", item.IssuesText);

        string path = Shot.Render(new PluginCompatView { DataContext = item }, "plugin_compat_card", 420, 120);
        Assert.True(new FileInfo(path).Length > 1500);
    }

    [AvaloniaFact]
    public void TheCpuFlag_ReachesTheCard()
    {
        var (host, fake) = FakePluginHost.Create();
        fake.Plugins.Add(Plugin("com.busy"));
        var page = new ActionStorePageViewModel(streamDeckHost: host);

        page.OnPluginCpuChanged(new PluginCpuUse("com.busy", 63, true));

        var item = page.StreamDeckPlugins[0];
        Assert.True(item.IsCpuHeavy);
        Assert.Contains("63", item.CpuText);
    }

    [AvaloniaFact]
    public async Task StartingOrStopping_DoesNotAskTheUninstallQuestion()
    {
        var (host, fake) = FakePluginHost.Create();
        fake.Plugins.Add(Plugin("com.x"));
        var confirm = new ConfirmDialogService();
        var page = new ActionStorePageViewModel(streamDeckHost: host, confirmDialog: confirm);

        var toggle = page.TogglePluginAsync(page.StreamDeckPlugins[0]);

        Assert.Null(confirm.Current);
        await toggle;
        Assert.Contains("StartPluginAsync", fake.Calls);
    }

    [AvaloniaFact]
    public async Task Uninstall_ListsTheKeysThatUseThePlugin()
    {
        var (host, fake) = FakePluginHost.Create();
        fake.Plugins.Add(Plugin("com.x"));
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var profile = profiles.GetActiveProfile();
        profile.Keys[4].Action = DeckActionType.StreamDeckPluginAction;
        profile.Keys[4].StreamDeckPluginUuid = "com.x";
        profiles.SaveProfile(profile);
        var confirm = new ConfirmDialogService();
        var page = new ActionStorePageViewModel(streamDeckHost: host, confirmDialog: confirm, profiles: profiles);

        var uninstall = page.UninstallPluginAsync(page.StreamDeckPlugins[0]);

        Assert.NotNull(confirm.Current);
        Assert.Contains("5", confirm.Current!.Message);
        Assert.Contains(profile.Name, confirm.Current.Message);
        confirm.Cancel();
        await uninstall;
        Assert.DoesNotContain("UninstallPluginAsync", fake.Calls);
    }

    [AvaloniaFact]
    public async Task NodeIsDownloaded_OnlyAfterTheUserAgrees()
    {
        var (host, fake) = FakePluginHost.Create();
        fake.Plugins.Add(Plugin("com.node", node: true));
        var confirm = new ConfirmDialogService();
        int downloads = 0;
        var page = new ActionStorePageViewModel(streamDeckHost: host, confirmDialog: confirm)
        {
            NodeInstaller = major => { downloads++; return Task.FromResult(new NodeInstallResult(true, @"C:\x\node.exe", "v" + major + ".11.1", null)); }
        };
        var item = page.StreamDeckPlugins[0];

        var declined = page.InstallNodeAsync(item);
        confirm.Cancel();
        await declined;
        Assert.Equal(0, downloads);

        var accepted = page.InstallNodeAsync(item);
        Assert.Contains("20", confirm.Current!.Message);
        confirm.Accept();
        await accepted;
        Assert.Equal(1, downloads);
        Assert.Contains("v20.11.1", page.StatusMessage);
    }
}

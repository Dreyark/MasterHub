using System;
using System.Linq;
using System.Threading.Tasks;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>
/// What happens right after a plugin package was installed: the plugin is started and the person is told the truth about it –
/// that it runs, that it cannot run here (and why), or that it started but never answered. A plugin made for another
/// system (a Linux <c>run.sh</c>) can never work on Windows, so it is taken out again instead of sitting there silent.
/// </summary>
public static class PluginInstallFollowUp
{
    public sealed record Result(string Message, bool Works, bool Removed);

    public static async Task<Result> RunAsync(IStreamDeckPluginHost host, StreamDeckInstalledPlugin installed)
    {
        var loc = LocalizationService.Instance;
        string name = string.IsNullOrWhiteSpace(installed.Name) ? installed.PluginUuid : installed.Name;

        var report = host.CheckCompatibility(installed.PluginUuid);
        if (!report.CanRun)
        {
            string why = string.Join(" ", report.Issues.Select(PluginTexts.Describe));
            if (report.Issues.Any(i => i.Issue == PluginIssue.OtherSystemOnly))
            {
                bool removed = await host.UninstallPluginAsync(installed.PluginUuid).ConfigureAwait(false);
                if (removed)
                    return new Result(string.Format(loc.GetString("Plugin_RemovedOtherSystem", "\"{0}\" was removed again: {1}"), name, why), false, true);
            }
            return new Result(string.Format(loc.GetString("Plugin_InstalledCannotRun", "\"{0}\" was installed but cannot run: {1}"), name, why), false, false);
        }

        await host.StartPluginAsync(installed.PluginUuid).ConfigureAwait(false);
        return host.IsPluginConnected(installed.PluginUuid)
            ? new Result(string.Format(loc.GetString("Plugin_InstalledOk", "\"{0}\" installed and started."), name), true, false)
            : new Result(string.Format(loc.GetString("Plugin_InstalledNoAnswer", "\"{0}\" was installed and started, but it did not report back - its keys may stay blank. See the plugin log."), name), false, false);
    }
}

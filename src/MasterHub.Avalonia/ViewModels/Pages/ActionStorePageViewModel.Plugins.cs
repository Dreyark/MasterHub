using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>Stream Deck host extras on the store page (T-31): CPU flag, "why it can't run", Node.js download with consent.</summary>
public partial class ActionStorePageViewModel
{
    private StreamDeckPluginItemViewModel? Find(string uuid) =>
        StreamDeckPlugins.FirstOrDefault(p => string.Equals(p.PluginUuid, uuid, StringComparison.OrdinalIgnoreCase));

    internal void OnPluginCpuChanged(PluginCpuUse use)
    {
        var item = Find(use.PluginUuid);
        if (item != null) item.Cpu = use;
    }

    internal void OnPluginCannotRun(string uuid, PluginCompatReport report)
    {
        var item = Find(uuid);
        if (item != null) item.Compat = report;
        string name = item?.Name ?? uuid;
        StatusMessage = string.Format(LocalizationService.Instance.GetString("Plugin_NotStarted", "\"{0}\" was not started: {1}"),
            name, string.Join(" ", report.Issues.Select(PluginTexts.Describe)));
    }

    /// <summary>
    /// T-31 b: downloads Node.js for a plugin that needs it – only after the user agreed to the download (≈ 30 MB from
    /// nodejs.org, checked against its published SHA-256, kept in MasterHub's own folder).
    /// </summary>
    [RelayCommand]
    public async Task InstallNodeAsync(StreamDeckPluginItemViewModel? item)
    {
        if (item == null) return;
        int major = item.RequiredNodeMajor;
        if (!await _confirmDialog.ConfirmAsync(ConfirmRequests.DownloadNode(major))) return;

        var loc = LocalizationService.Instance;
        StatusMessage = string.Format(loc.GetString("Plugin_NodeDownloading", "Downloading Node.js {0} from nodejs.org…"), major);
        var result = NodeInstaller != null
            ? await NodeInstaller(major)
            : await new NodeRuntimeInstaller().InstallAsync(major);

        StatusMessage = result.Success
            ? string.Format(loc.GetString("Plugin_NodeInstalled", "Node.js {0} is ready. Start the plugin again."), result.Version)
            : loc.GetString("Plugin_NodeFailed", "Node.js could not be downloaded: ") + result.Error;
        if (result.Success) RefreshPlugins();
    }
}

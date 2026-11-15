using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>Localised words for plugin compatibility (T-31 e/f): the badge and one line per reason.</summary>
public static class PluginTexts
{
    public static string Verdict(PluginVerdict verdict)
    {
        var loc = LocalizationService.Instance;
        return verdict switch
        {
            PluginVerdict.Runs => loc.GetString("Plugin_VerdictRuns", "Runs here"),
            PluginVerdict.Partial => loc.GetString("Plugin_VerdictPartial", "May partly work"),
            _ => loc.GetString("Plugin_VerdictCannot", "Cannot run here")
        };
    }

    public static string Describe(PluginIssueItem item)
    {
        var loc = LocalizationService.Instance;
        return item.Issue switch
        {
            PluginIssue.OtherSystemOnly => string.Format(loc.GetString("Plugin_IssueOtherSystem", "Made only for {0}, not for Windows - it cannot run here."), item.Detail),
            PluginIssue.NodeMissing => string.Format(loc.GetString("Plugin_IssueNodeMissing", "Needs Node.js {0}, which is not installed. MasterHub can download it for you."), item.Detail),
            PluginIssue.NodeTooOld => string.Format(loc.GetString("Plugin_IssueNodeTooOld", "Needs Node.js {0}; this PC has {1}. MasterHub can download a newer one for you."),
                item.Detail.Split('|')[0], item.Detail.Contains('|') ? item.Detail.Split('|')[1] : "?"),
            PluginIssue.NoBrowser => loc.GetString("Plugin_IssueNoBrowser", "An HTML plugin: needs Microsoft Edge or Google Chrome."),
            PluginIssue.ExecutableMissing => string.Format(loc.GetString("Plugin_IssueExeMissing", "Its program file ({0}) is missing – reinstall the plugin."), item.Detail),
            PluginIssue.NeedsNewerStreamDeck => string.Format(loc.GetString("Plugin_IssueNewerSd", "Made for Stream Deck {0} or newer; some features may not work."), item.Detail),
            _ => item.Issue.ToString()
        };
    }
}

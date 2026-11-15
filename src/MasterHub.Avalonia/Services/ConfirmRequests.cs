using System.Linq;
using MasterHub.Core.Dialogs;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.Services;

/// <summary>Builds localised <see cref="ConfirmRequest"/>s. Keys: <c>Confirm_&lt;Name&gt;_Title / _Message / _Button</c>; the
/// message may contain <c>{0}</c> (the name of the thing being deleted).</summary>
public static class ConfirmRequests
{
    public static ConfirmRequest Create(string name, string defaultTitle, string defaultMessage, string defaultButton, string? subject = null, bool destructive = true)
    {
        var loc = LocalizationService.Instance;
        string message = loc.GetString($"Confirm_{name}_Message", defaultMessage);
        if (subject != null) message = string.Format(message, subject);

        return new ConfirmRequest(
            loc.GetString($"Confirm_{name}_Title", defaultTitle),
            message,
            loc.GetString($"Confirm_{name}_Button", defaultButton),
            loc.GetString("Confirm_Cancel", "Cancel"),
            destructive);
    }

    public static ConfirmRequest DeleteProfile(string profileName) =>
        Create("DeleteProfile", "Delete profile?", "Profile \"{0}\" will be deleted. A copy stays in Settings > Backups.", "Delete profile", profileName);

    public static ConfirmRequest DeletePage() =>
        Create("DeletePage", "Delete page?", "The current page and all its keys will be removed from this profile.", "Delete page");

    public static ConfirmRequest DeleteFolder() =>
        Create("DeleteFolder", "Delete folder?", "The current folder and all its keys will be removed from this profile.", "Delete folder");

    public static ConfirmRequest UninstallPlugin(string pluginName) =>
        Create("UninstallPlugin", "Uninstall plugin?", "Plugin \"{0}\" and its files will be removed.", "Uninstall", pluginName);

    /// <summary>T-31 c: the uninstall question also lists the keys that use the plugin.</summary>
    public static ConfirmRequest UninstallPlugin(string pluginName, System.Collections.Generic.IReadOnlyList<MasterHub.Core.Integrations.StreamDeck.PluginUsage> usages)
    {
        var request = UninstallPlugin(pluginName);
        if (usages.Count == 0) return request;

        const int Shown = 10;
        var loc = LocalizationService.Instance;
        string line = loc.GetString("Confirm_UninstallPlugin_KeyLine", "• {0}, page {1}, key {2}");
        string list = string.Join("\n", usages.Take(Shown).Select(u => string.Format(line, u.ProfileName, u.Page, u.Key)))
                      + (usages.Count > Shown ? $"\n… +{usages.Count - Shown}" : "");
        string header = string.Format(loc.GetString("Confirm_UninstallPlugin_Keys", "These {0} key(s) use it and will stop working:"), usages.Count);
        return request with { Message = request.Message + "\n\n" + header + "\n" + list };
    }

    /// <summary>T-31 b: asks before Node.js is downloaded for a plugin.</summary>
    public static ConfirmRequest DownloadNode(int major) =>
        Create("DownloadNode", "Download Node.js?",
            "This plugin needs Node.js {0}. MasterHub can download it from nodejs.org (about 30 MB), check it against the published checksum and keep it in its own folder – nothing is installed for the whole system.",
            "Download Node.js", major.ToString(), destructive: false);

    public static ConfirmRequest ImportPrograms(System.Collections.Generic.IReadOnlyList<MasterHub.Core.Profiles.Packages.ProgramRisk> risks)
    {
        const int Shown = 12;
        var lines = risks.Take(Shown).Select(r => $"• page {r.Page}, key {r.Key}: {r.Action} {r.Detail}".TrimEnd());
        string list = string.Join("\n", lines) + (risks.Count > Shown ? $"\n… +{risks.Count - Shown}" : "");
        return Create("ImportPrograms", "This profile can run programs", "Importing it will not run anything, but these keys can start programs, open files or web pages, or type text when pressed:\n\n{0}\n\nImport only profiles from people you trust.", "Import profile", list);
    }

    public static ConfirmRequest AllowCommand(string commandLine) =>
        Create("AllowCommand", "Allow this command?", "A live-value key wants to run this command line every time it refreshes:\n\n{0}\n\nOnly allow commands you trust.", "Allow command", commandLine, destructive: false);

    public static ConfirmRequest RestoreBackup(string backupName) =>
        Create("RestoreBackup", "Restore backup?", "Profiles and settings will be replaced by the backup \"{0}\". The current state is saved first, so you can undo this.", "Restore backup", backupName);
}

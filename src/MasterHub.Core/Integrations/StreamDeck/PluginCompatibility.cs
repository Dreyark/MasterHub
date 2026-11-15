using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>Why a plugin cannot run, or what may not work (T-31 f).</summary>
public enum PluginIssue
{
    /// <summary>The plugin is made for another system (Linux, macOS), not for Windows. The detail names the system(s).</summary>
    OtherSystemOnly,
    /// <summary>A Node.js plugin, and no Node.js was found (MasterHub can download one with your consent).</summary>
    NodeMissing,
    /// <summary>A Node.js plugin that needs a newer Node.js than the one found.</summary>
    NodeTooOld,
    /// <summary>An HTML plugin, and neither Edge nor Chrome is installed.</summary>
    NoBrowser,
    /// <summary>The plugin's program file is missing from its folder.</summary>
    ExecutableMissing,
    /// <summary>The plugin asks for a newer Stream Deck app than MasterHub imitates; some features may not work.</summary>
    NeedsNewerStreamDeck
}

/// <summary>The badge of a plugin in the catalog / store (T-31 e).</summary>
public enum PluginVerdict { Runs, Partial, CannotRun }

/// <summary>One issue with its details (e.g. the Node version needed and found).</summary>
public sealed record PluginIssueItem(PluginIssue Issue, string Detail = "");

public sealed record PluginCompatReport(PluginVerdict Verdict, IReadOnlyList<PluginIssueItem> Issues)
{
    public bool CanRun => Verdict != PluginVerdict.CannotRun;
}

/// <summary>What the compatibility check needs to know about this PC (fakes in tests).</summary>
public interface IPluginRuntimeEnvironment
{
    NodeResolution ResolveNode(int requiredMajor);
    bool HasBrowser { get; }
    bool FileExists(string path);
}

/// <summary>
/// Clear "why it can't run" reasons for Stream Deck plugins (T-31 f) and the badge they lead to (T-31 e): a plugin is checked
/// before it is started – instead of failing silently in the background – for the OS it supports, its runtime (Node.js
/// version, a browser for HTML plugins, its program file) and the Stream Deck version it expects.
/// </summary>
public static class PluginCompatibility
{
    /// <summary>The Stream Deck app version MasterHub reports to plugins (in the <c>-info</c> argument).</summary>
    public static readonly Version ReportedStreamDeckVersion = new(6, 5, 0);

    public static PluginCompatReport Check(StreamDeckInstalledPlugin plugin, IPluginRuntimeEnvironment env)
    {
        var issues = new List<PluginIssueItem>();
        var manifest = plugin.Manifest ?? new StreamDeckManifest();

        var platforms = (manifest.OS ?? new()).Select(o => (o.Platform ?? "").Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
        if (platforms.Count > 0 && !platforms.Contains("windows"))
            issues.Add(new PluginIssueItem(PluginIssue.OtherSystemOnly, string.Join(", ", platforms.Select(SystemName))));
        else if (platforms.Count == 0 && IsShellLauncher(plugin))
            issues.Add(new PluginIssueItem(PluginIssue.OtherSystemOnly, "Linux"));

        if (plugin.IsNodeJs)
        {
            int major = NodeResolver.RequiredMajor(manifest.Nodejs?.Version);
            var node = env.ResolveNode(major);
            if (node.Problem == NodeProblem.Missing) issues.Add(new PluginIssueItem(PluginIssue.NodeMissing, major.ToString()));
            else if (node.Problem == NodeProblem.TooOld) issues.Add(new PluginIssueItem(PluginIssue.NodeTooOld, $"{major}|{node.Version}"));
        }
        else if (plugin.IsHtml)
        {
            if (!env.HasBrowser) issues.Add(new PluginIssueItem(PluginIssue.NoBrowser));
        }
        else if (string.IsNullOrWhiteSpace(plugin.ExecutablePath) || !env.FileExists(plugin.ExecutablePath))
        {
            issues.Add(new PluginIssueItem(PluginIssue.ExecutableMissing, Path.GetFileName(plugin.ExecutablePath ?? "")));
        }

        if (Version.TryParse(manifest.Software?.MinimumVersion ?? "", out var needed) && needed > ReportedStreamDeckVersion)
            issues.Add(new PluginIssueItem(PluginIssue.NeedsNewerStreamDeck, needed.ToString()));

        return new PluginCompatReport(VerdictOf(issues), issues);
    }

    /// <summary>A plugin whose only program is a shell script (<c>run.sh</c>): it was written for Linux/macOS and Windows cannot start it.</summary>
    private static bool IsShellLauncher(StreamDeckInstalledPlugin plugin) =>
        !plugin.IsNodeJs && !plugin.IsHtml
        && (plugin.ExecutablePath ?? "").EndsWith(".sh", StringComparison.OrdinalIgnoreCase);

    private static string SystemName(string platform) => platform switch
    {
        "linux" => "Linux",
        "mac" or "macos" or "macosx" or "darwin" => "macOS",
        _ => platform
    };

    private static PluginVerdict VerdictOf(IReadOnlyList<PluginIssueItem> issues)
    {
        if (issues.Any(i => i.Issue is PluginIssue.OtherSystemOnly or PluginIssue.NodeMissing or PluginIssue.NodeTooOld or PluginIssue.NoBrowser or PluginIssue.ExecutableMissing))
            return PluginVerdict.CannotRun;
        return issues.Count > 0 ? PluginVerdict.Partial : PluginVerdict.Runs;
    }

    /// <summary>
    /// The compatibility table (T-31 e) as Markdown, for <c>docs/STREAMDECK_COMPAT.md</c>: one row per plugin with its
    /// runtime, the automatic verdict and the reasons. "Tried" is filled in by a person after a real test.
    /// </summary>
    public static string ToMarkdown(IEnumerable<(StreamDeckInstalledPlugin Plugin, PluginCompatReport Report)> rows)
    {
        var lines = new List<string>
        {
            "| Plugin | Version | Runtime | Automatic check | Reasons | Tried in MasterHub |",
            "| :-- | :-- | :-- | :-: | :-- | :-- |"
        };
        foreach (var (plugin, report) in rows.OrderBy(r => r.Plugin.Name, StringComparer.OrdinalIgnoreCase))
        {
            string runtime = plugin.IsNodeJs ? "Node.js " + NodeResolver.RequiredMajor(plugin.Manifest?.Nodejs?.Version) : plugin.IsHtml ? "HTML" : "native";
            string verdict = report.Verdict switch { PluginVerdict.Runs => "✅ runs", PluginVerdict.Partial => "⚠️ partial", _ => "❌ cannot run" };
            string reasons = report.Issues.Count == 0 ? "–" : string.Join("; ", report.Issues.Select(i => i.Issue + (i.Detail.Length > 0 ? " (" + i.Detail + ")" : "")));
            lines.Add($"| {Cell(plugin.Name)} | {Cell(plugin.Version)} | {runtime} | {verdict} | {Cell(reasons)} | not yet |");
        }
        return string.Join("\n", lines) + "\n";
    }

    private static string Cell(string? text) => (text ?? "").Replace("|", "\\|").Replace("\n", " ");
}

/// <summary>The real environment of the compatibility check.</summary>
public sealed class SystemPluginRuntimeEnvironment : IPluginRuntimeEnvironment
{
    private readonly Func<bool> _hasBrowser;

    public SystemPluginRuntimeEnvironment(Func<bool> hasBrowser)
    {
        _hasBrowser = hasBrowser;
    }

    public NodeResolution ResolveNode(int requiredMajor) => NodeResolver.Resolve(RealNodeFileSystem.Instance, requiredMajor);
    public bool HasBrowser => _hasBrowser();
    public bool FileExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }
}

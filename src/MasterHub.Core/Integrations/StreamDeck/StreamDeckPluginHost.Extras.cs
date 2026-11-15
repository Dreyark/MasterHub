using System;
using System.Linq;
using System.Threading;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>
/// Stream Deck host extras (T-31): multi-state actions (d), the CPU watchdog (a) and the "why it can't run" check (f).
/// </summary>
public sealed partial class StreamDeckPluginHost
{
    /// <summary>How often the CPU of running plugins is sampled.</summary>
    public static readonly TimeSpan CpuSampleInterval = TimeSpan.FromSeconds(5);

    private readonly PluginKeyStates _keyStates = new();
    private PluginCpuWatchdog? _cpuWatchdog;
    private Timer? _cpuTimer;

    public event Action<PluginCpuUse>? PluginCpuChanged;
    public event Action<string, PluginCompatReport>? PluginCannotRun;

    public int GetKeyState(int keyIndex) => _keyStates.Get(keyIndex);

    public PluginCompatReport CheckCompatibility(string pluginUuid)
    {
        if (!_plugins.TryGetValue(pluginUuid, out var plugin)) return new PluginCompatReport(PluginVerdict.Runs, Array.Empty<PluginIssueItem>());
        return PluginCompatibility.Check(plugin, new SystemPluginRuntimeEnvironment(() => FindBrowserExecutable() != null));
    }

    public bool IsPluginConnected(string pluginUuid) => _server.IsPluginConnected(pluginUuid);

    public PluginCpuUse GetCpuUse(string pluginUuid) => _cpuWatchdog?.Current(pluginUuid) ?? new PluginCpuUse(pluginUuid, 0, false);

    /// <summary>T-31 d: the picture of the state the key is in (from the action's manifest), or <c>null</c>.</summary>
    public string? GetStateImagePath(int keyIndex)
    {
        if (!_activeKeyAssignments.TryGetValue(keyIndex, out var a) || !_plugins.TryGetValue(a.PluginUuid, out var plugin)) return null;
        var action = GetActionManifest(a.PluginUuid, a.ActionUuid);
        if (action == null || action.States.Count == 0) return null;
        var state = action.States[PluginKeyStates.Clamp(_keyStates.Get(keyIndex), action.States.Count)];
        return ResolveStateImage(plugin.FolderPath, state.Image, System.IO.File.Exists);
    }

    /// <summary>A manifest image name (<c>imgs/state1</c>) to a file: as written, then <c>.png</c>, <c>@2x.png</c>, <c>.svg</c>.</summary>
    public static string? ResolveStateImage(string pluginFolder, string? imageName, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(pluginFolder) || string.IsNullOrWhiteSpace(imageName)) return null;
        string name = imageName.Trim().Replace('/', System.IO.Path.DirectorySeparatorChar);
        if (System.IO.Path.IsPathRooted(name) || name.Contains("..")) return null;   // stays inside the plugin folder
        string basePath = System.IO.Path.Combine(pluginFolder, name);
        foreach (string candidate in new[] { basePath, basePath + ".png", basePath + "@2x.png", basePath + ".svg" })
        {
            if (System.IO.Path.HasExtension(candidate) && fileExists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Number of states of the action on a key (from its manifest).</summary>
    private (int Count, bool DisableAutomatic) StatesOf(string pluginUuid, string actionUuid)
    {
        var action = GetActionManifest(pluginUuid, actionUuid);
        return (action?.States.Count ?? 0, action?.DisableAutomaticStates ?? false);
    }

    /// <summary>Checked before a plugin process is started: a plugin that cannot run is not started, and the reason is told.</summary>
    private bool EnsureCanRun(string pluginUuid)
    {
        var report = CheckCompatibility(pluginUuid);
        if (report.CanRun) return true;

        string reasons = string.Join("; ", report.Issues.Select(i => i.Issue + (i.Detail.Length > 0 ? " (" + i.Detail + ")" : "")));
        PluginLogReceived?.Invoke(pluginUuid, "Wtyczka nie została uruchomiona: " + reasons);
        PluginCannotRun?.Invoke(pluginUuid, report);
        return false;
    }

    private void EnsureCpuWatchdog()
    {
        if (_cpuTimer != null) return;
        _cpuWatchdog = new PluginCpuWatchdog(new SystemProcessCpuSampler());
        _cpuTimer = new Timer(_ => SampleCpu(), null, CpuSampleInterval, CpuSampleInterval);
    }

    private void SampleCpu()
    {
        try
        {
            var running = _runningProcesses
                .Where(p => { try { return !p.Value.HasExited; } catch { return false; } })
                .ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var change in _cpuWatchdog!.Sample(running))
            {
                if (change.IsHeavy)
                    PluginLogReceived?.Invoke(change.PluginUuid, $"Wtyczka zużywa dużo procesora: {change.PercentOfOneCore:0} % jednego rdzenia (średnio z ostatnich ~30 s).");
                PluginCpuChanged?.Invoke(change);
            }
        }
        catch
        {
            // the watchdog is a help; it must never disturb the plugins
        }
    }

    private void StopCpuWatchdog()
    {
        _cpuTimer?.Dispose();
        _cpuTimer = null;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MasterHub.Core.Apps;

/// <summary>An application that currently has a visible window.</summary>
/// <param name="ProcessName">Process name without extension (what a Smart Profile rule matches).</param>
/// <param name="Title">Title of its main window.</param>
/// <param name="ExePath">Full path of the executable when it could be read (used for the icon), otherwise <c>null</c>.</param>
public sealed record RunningApp(string ProcessName, string Title, string? ExePath);

/// <summary>Lists running applications with a visible window, for the "Link app…" picker.</summary>
public static class RunningApps
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
        "LockApp", "MasterHub.Avalonia", "MasterHub"
    };

    public static IReadOnlyList<RunningApp> List()
    {
        var result = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                if (process.Id == Environment.ProcessId || Ignored.Contains(process.ProcessName)) continue;

                string? path = null;
                try { path = process.MainModule?.FileName; } catch { /* protected / elevated process: no path, no icon */ }

                result.TryAdd(process.ProcessName, new RunningApp(process.ProcessName, process.MainWindowTitle, path));
            }
            catch
            {
                // the process exited while it was being inspected
            }
            finally
            {
                process.Dispose();
            }
        }

        return result.Values.OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

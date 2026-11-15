using System;

namespace MasterHub.Core.SystemControl;

/// <summary>What the start-up renewal found and did.</summary>
public readonly record struct AutostartRenewal(bool Enabled, bool Rewritten, string? Command);

/// <summary>
/// The autostart entry is renewed on every start (T-34 #4): when MasterHub moved (an update into a new folder, a portable
/// copy) the old entry would start nothing. An entry that exists is rewritten with the current program path, keeping its
/// "start minimised" choice; no entry means autostart is off and nothing is written. The result is logged.
/// </summary>
public static class AutostartEntry
{
    public const string AutostartArg = "--autostart";
    public const string MinimizedArg = "--minimized";

    public static string BuildCommand(string exePath, bool startMinimized) =>
        $"\"{exePath}\" {AutostartArg}" + (startMinimized ? " " + MinimizedArg : "");

    public static bool IsMinimized(string? command) =>
        command != null && command.Contains(MinimizedArg, StringComparison.OrdinalIgnoreCase);

    /// <summary>Renews <paramref name="current"/> (the value of the Run entry, or <c>null</c>) for <paramref name="exePath"/>.</summary>
    public static AutostartRenewal Renew(string? current, string exePath, Action<string> write)
    {
        if (string.IsNullOrWhiteSpace(current)) return new AutostartRenewal(false, false, null);
        if (string.IsNullOrWhiteSpace(exePath)) return new AutostartRenewal(true, false, current);

        string wanted = BuildCommand(exePath, IsMinimized(current));
        if (string.Equals(wanted, current.Trim(), StringComparison.OrdinalIgnoreCase)) return new AutostartRenewal(true, false, current);

        write(wanted);
        return new AutostartRenewal(true, true, wanted);
    }

    /// <summary>The log line for a renewal.</summary>
    public static string Describe(AutostartRenewal r) =>
        !r.Enabled ? "[Autostart] off (no entry in HKCU\\…\\Run)"
        : r.Rewritten ? "[Autostart] on – entry renewed for the current program path"
        : "[Autostart] on – entry up to date";
}

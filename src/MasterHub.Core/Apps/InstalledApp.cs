using System;
using System.IO;
using System.Text.Json.Serialization;

namespace MasterHub.Core.Apps;

/// <summary>Where an installed app / game was found.</summary>
public enum InstalledAppKind
{
    /// <summary>A shortcut in the Start menu (current user or all users).</summary>
    StartMenu,
    /// <summary>A Microsoft Store (AppX/MSIX) app.</summary>
    Store,
    Steam,
    Epic,
    Gog
}

/// <summary>
/// One thing that is installed on this PC and can be launched or linked to a profile.
/// </summary>
/// <param name="Id">Stable id, e.g. <c>steam:730</c>, <c>store:Microsoft.WindowsCalculator_8wekyb3d8bbwe!App</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Kind">Source.</param>
/// <param name="LaunchTarget">What to pass to <c>ShellExecute</c>: a file path, <c>steam://rungameid/…</c>, a launcher link or <c>shell:AppsFolder\AUMID</c>.</param>
/// <param name="ExePath">Executable (when known): used for the icon and to match the foreground process (Smart Profiles).</param>
public sealed record InstalledApp(string Id, string Name, InstalledAppKind Kind, string LaunchTarget, string? ExePath = null)
{
    /// <summary>
    /// Cover art from the <b>launcher's own local cache on this PC</b>, for local display only. It is never serialised
    /// (<see cref="JsonIgnoreAttribute"/>): not into <c>installed.json</c>, not into profiles, exports or backups.
    /// </summary>
    [JsonIgnore]
    public string? CoverPath { get; init; }

    /// <summary>Executable name without extension (what <c>SmartProfileRule.ProcessName</c> matches), or empty.</summary>
    [JsonIgnore]
    public string ProcessName => string.IsNullOrWhiteSpace(ExePath) ? "" : Path.GetFileNameWithoutExtension(ExePath);

    [JsonIgnore]
    public bool IsGame => Kind is InstalledAppKind.Steam or InstalledAppKind.Epic or InstalledAppKind.Gog;
}

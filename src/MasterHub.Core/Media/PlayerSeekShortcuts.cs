using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Media;

/// <summary>A player and the keys it uses to jump forward / back.</summary>
public sealed record PlayerSeek(string Id, string Name, IReadOnlyList<string> Processes, string Forward, string Back, int Seconds);

/// <summary>
/// Seeking through the players' own shortcuts (T-17 e): the program in front gets the key it uses for "forward" /
/// "back". Our own table, written from the players' documented default shortcuts; anything not listed gets the arrow keys.
/// </summary>
public static class PlayerSeekShortcuts
{
    public static readonly IReadOnlyList<PlayerSeek> Players = new PlayerSeek[]
    {
        new("vlc", "VLC (10 s)", new[] { "vlc" }, "Alt+Right", "Alt+Left", 10),
        new("vlc-short", "VLC (3 s)", Array.Empty<string>(), "Shift+Right", "Shift+Left", 3),
        new("wmp", "Media Player", new[] { "microsoft.media.player", "mediaplayer" }, "Ctrl+Right", "Ctrl+Left", 10),
        new("wmp-legacy", "Windows Media Player (legacy)", new[] { "wmplayer" }, "Ctrl+Shift+F", "Ctrl+Shift+B", 10),
        new("mpc-hc", "MPC-HC", new[] { "mpc-hc64", "mpc-hc" }, "Right", "Left", 5),
        new("mpc-be", "MPC-BE", new[] { "mpc-be64", "mpc-be" }, "Right", "Left", 5),
        new("potplayer", "PotPlayer", new[] { "potplayermini64", "potplayermini", "potplayer64" }, "Right", "Left", 5),
        new("kmplayer", "KMPlayer", new[] { "kmplayer", "kmplayer64x" }, "Right", "Left", 5),
        new("gom", "GOM Player", new[] { "gom", "gomplayer" }, "Right", "Left", 5),
        new("youtube", "YouTube (browser)", Array.Empty<string>(), "L", "J", 10),
        new("spotify", "Spotify", new[] { "spotify" }, "Shift+Right", "Shift+Left", 5),
        new("netflix", "Netflix", new[] { "netflix" }, "Right", "Left", 10),
    };

    /// <summary>Used for a program that is not in the table.</summary>
    public static readonly PlayerSeek Generic = new("generic", "Arrow keys", Array.Empty<string>(), "Right", "Left", 5);

    private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };

    /// <summary>The player for a process (name without <c>.exe</c>, any case); a browser showing YouTube uses YouTube's keys.</summary>
    public static PlayerSeek ForProcess(string? processName, string? windowTitle = null)
    {
        string p = (processName ?? "").Trim().ToLowerInvariant();
        if (p.EndsWith(".exe", StringComparison.Ordinal)) p = p[..^4];
        if (Browsers.Contains(p) && (windowTitle ?? "").Contains("YouTube", StringComparison.OrdinalIgnoreCase))
            return Players.First(x => x.Id == "youtube");
        return Players.FirstOrDefault(x => x.Processes.Contains(p)) ?? Generic;
    }

    public static PlayerSeek? ById(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Players.FirstOrDefault(x => x.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
}

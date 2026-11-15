using System;
using System.Collections.Generic;
using System.Linq;
using MasterHub.Core.Apps;

namespace MasterHub.Core.Profiles.Draft;

/// <summary>What the user mostly wants the deck for. The setup tour asks this one question and builds a draft from the answer.</summary>
public enum TourFocus
{
    /// <summary>Everyday desk (the default answer).</summary>
    Desk = 0,
    Streaming = 1,
    Meetings = 2,
    Gaming = 3,
    Monitoring = 4,
    SmartHome = 5
}

/// <summary>Whether the tour was offered, so it is never forced twice (it can always be run again from Settings).</summary>
public enum TourState
{
    NotOffered = 0,
    Done = 1,
    /// <summary>The user said "not now / do not ask again".</summary>
    Dismissed = 2
}

/// <summary>What the tour looks at to suggest an answer.</summary>
/// <param name="RunningProcesses">Process names (without <c>.exe</c>) of the programs that have a window now.</param>
/// <param name="InstalledApps">Installed programs and games (T-36).</param>
public sealed record TourContext(IReadOnlyCollection<string> RunningProcesses, IReadOnlyCollection<InstalledApp> InstalledApps)
{
    public static TourContext Empty { get; } = new(Array.Empty<string>(), Array.Empty<InstalledApp>());
}

/// <summary>The tour's brain: which answer to suggest, and which ready-made profile an answer maps to.</summary>
public static class TourAdvisor
{
    // Process names, compared without case. Streaming and call programs are separate lists on purpose: a program that is
    // open right now says more than one that is merely installed.
    private static readonly string[] StreamingApps = { "obs64", "obs32", "obs", "Streamlabs Desktop", "Streamlabs OBS", "TwitchStudio" };
    private static readonly string[] CallApps = { "Teams", "ms-teams", "Zoom", "Slack", "Webex", "webexmta" };
    private static readonly string[] ChatApps = { "Discord", "DiscordPTB", "DiscordCanary" };

    /// <summary>Games installed from a launcher at which the tour calls the user a gamer.</summary>
    public const int GamesForGamer = 3;

    /// <summary>The id of the embedded template an answer builds the draft from.</summary>
    public static string TemplateId(TourFocus focus) => focus switch
    {
        TourFocus.Streaming => "obs-streaming",
        TourFocus.Meetings => "discord-meetings",
        TourFocus.Gaming => "game-action-bar",
        TourFocus.Monitoring => "system-monitor",
        TourFocus.SmartHome => "smart-home",
        _ => "everyday-desk"
    };

    /// <summary>
    /// The answer to pre-select, from what the app sees. Programs that are open win over programs that are only installed;
    /// with nothing to go on it is <see cref="TourFocus.Desk"/>. The user can pick any answer anyway.
    /// </summary>
    public static TourFocus Suggest(TourContext context)
    {
        bool Running(IEnumerable<string> names) => context.RunningProcesses.Any(p => names.Contains(p, StringComparer.OrdinalIgnoreCase));
        bool Installed(IEnumerable<string> names) => context.InstalledApps.Any(a => names.Contains(a.ProcessName, StringComparer.OrdinalIgnoreCase));

        if (Running(StreamingApps)) return TourFocus.Streaming;
        if (Running(CallApps)) return TourFocus.Meetings;
        if (context.InstalledApps.Count(a => a.IsGame) >= GamesForGamer) return TourFocus.Gaming;
        if (Installed(StreamingApps)) return TourFocus.Streaming;
        if (Installed(CallApps) || Installed(ChatApps)) return TourFocus.Meetings;
        return TourFocus.Desk;
    }

    /// <summary>The names of the programs behind the suggestion (shown as "I can see: OBS, Discord…").</summary>
    public static IReadOnlyList<string> Noticed(TourContext context)
    {
        var all = StreamingApps.Concat(CallApps).Concat(ChatApps).ToList();
        var seen = new List<string>();

        foreach (string process in context.RunningProcesses)
            if (all.Contains(process, StringComparer.OrdinalIgnoreCase) && !seen.Contains(process, StringComparer.OrdinalIgnoreCase)) seen.Add(process);

        foreach (var app in context.InstalledApps)
            if (all.Contains(app.ProcessName, StringComparer.OrdinalIgnoreCase) && !seen.Contains(app.ProcessName, StringComparer.OrdinalIgnoreCase)) seen.Add(app.ProcessName);

        return seen;
    }

    /// <summary>
    /// Empties the keys of <paramref name="profile"/> whose index is in <paramref name="dropped"/> ("each part can be kept or dropped").
    /// It works on the profile it is given, so call it on a freshly built draft.
    /// </summary>
    public static void DropKeys(ProfileModel profile, ISet<int> dropped)
    {
        void Clear(IEnumerable<KeyAssignmentModel>? keys)
        {
            if (keys == null) return;
            foreach (var key in keys.Where(k => dropped.Contains(k.Index)))
            {
                key.Action = Actions.DeckActionType.None;
                key.ActionParameter = "";
                key.CustomTitle = "";
                key.CustomSubtitle = "";
                key.BackgroundColorHex = "#000000";
            }
        }

        Clear(profile.Keys);
        foreach (var page in profile.Pages ?? new()) Clear(page.Keys);
    }
}

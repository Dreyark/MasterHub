using System;

namespace MasterHub.Core.Profiles.Draft;

/// <summary>How a draft was started.</summary>
public enum DraftMode
{
    /// <summary>Stays until the user keeps or throws it away.</summary>
    Preview = 0,
    /// <summary>Puts itself away after <see cref="DraftInfo.TrialSeconds"/> unless it was kept ("20-second try-out").</summary>
    Trial = 1
}

/// <summary>
/// A draft (T-08) is a profile that is shown on the deck and in the window <b>without being saved among the user's profiles</b>.
/// It lives in its own file, so a crash leaves it there and the next start can offer it back; the user's own profile is never touched.
/// </summary>
/// <param name="DraftId">Id of the draft profile (becomes a new id when it is kept).</param>
/// <param name="Name">Name of the draft profile.</param>
/// <param name="OriginProfileId">The profile that was active before; it comes back when the draft is thrown away.</param>
/// <param name="StartedUtc">When the draft was started (the trial countdown runs from here).</param>
/// <param name="TrialSeconds">Length of a <see cref="DraftMode.Trial"/>; 0 for a preview.</param>
public sealed record DraftInfo(string DraftId, string Name, string OriginProfileId, DraftMode Mode, DateTime StartedUtc, int TrialSeconds)
{
    public bool IsTrial => Mode == DraftMode.Trial && TrialSeconds > 0;

    /// <summary>Seconds left of a trial (0 for a preview or when it ran out).</summary>
    public int SecondsLeft(DateTime nowUtc) =>
        IsTrial ? Math.Max(0, TrialSeconds - (int)Math.Floor((nowUtc - StartedUtc).TotalSeconds)) : 0;

    public bool IsExpired(DateTime nowUtc) => IsTrial && SecondsLeft(nowUtc) <= 0;
}

/// <summary>What is written to <c>drafts/draft.json</c>.</summary>
public sealed class DraftEnvelope
{
    public DraftInfo? Info { get; set; }
    public ProfileModel? Profile { get; set; }
}

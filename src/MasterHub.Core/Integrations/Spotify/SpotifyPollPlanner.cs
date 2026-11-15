using System;

namespace MasterHub.Core.Integrations.Spotify;

/// <summary>What the last Spotify playback request told us.</summary>
public enum SpotifyPollOutcome
{
    /// <summary>A track is loaded (playing or paused).</summary>
    Playback,
    /// <summary>Nothing is playing on any device.</summary>
    NoPlayback,
    /// <summary>The request failed, was refused or the token is being renewed.</summary>
    Failed
}

/// <summary>
/// Decides when to ask Spotify again (T-34 #3). A fixed 2 s poll was ≈ 1 800 requests an hour and brought HTTP 429 answers. Nothing on
/// a key needs a per-second value (no progress bar), and pressing one of our keys already refreshes at once, so a poll is only needed to
/// catch a track change and what was done in another app: shortly after the track is due to end, otherwise every 15–30 s.
/// Pure (no clock, no network) so it is easy to test.
/// </summary>
public static class SpotifyPollPlanner
{
    /// <summary>Longest wait while a track is playing: a pause made on a phone shows up within this.</summary>
    public static readonly TimeSpan PlayingMax = TimeSpan.FromSeconds(15);

    /// <summary>Wait while a track is paused or nothing plays.</summary>
    public static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(30);

    /// <summary>Retry after a failed request.</summary>
    public static readonly TimeSpan FailureWait = TimeSpan.FromSeconds(10);

    /// <summary>Spotify refreshes its position a little after the track really changes.</summary>
    private static readonly TimeSpan EndMargin = TimeSpan.FromSeconds(1);

    /// <param name="floor">The user's "poll every n s" setting: never poll faster than this.</param>
    /// <param name="outcome">What the last request gave.</param>
    /// <param name="isPlaying">Whether a track is playing now.</param>
    /// <param name="progressMs">Position in the track at the time of the request (0 when unknown).</param>
    /// <param name="durationMs">Length of the track (0 when unknown).</param>
    /// <param name="rateLimitedFor">How long Spotify asked us to stay quiet (Retry-After), zero when not limited.</param>
    public static TimeSpan Next(TimeSpan floor, SpotifyPollOutcome outcome, bool isPlaying, long progressMs, long durationMs,
        TimeSpan rateLimitedFor = default)
    {
        if (floor < TimeSpan.FromSeconds(1)) floor = TimeSpan.FromSeconds(1);

        TimeSpan wait = outcome switch
        {
            SpotifyPollOutcome.Failed => FailureWait,
            SpotifyPollOutcome.NoPlayback => IdleWait,
            _ when !isPlaying => IdleWait,
            _ => UntilTrackEnd(progressMs, durationMs)
        };

        if (rateLimitedFor > wait) wait = rateLimitedFor;
        return wait < floor ? floor : wait;
    }

    private static TimeSpan UntilTrackEnd(long progressMs, long durationMs)
    {
        if (durationMs <= 0 || progressMs < 0 || progressMs > durationMs) return PlayingMax;

        var remaining = TimeSpan.FromMilliseconds(durationMs - progressMs) + EndMargin;
        return remaining < PlayingMax ? remaining : PlayingMax;
    }
}

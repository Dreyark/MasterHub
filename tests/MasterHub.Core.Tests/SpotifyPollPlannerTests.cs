using MasterHub.Core.Integrations.Spotify;

namespace MasterHub.Core.Tests;

/// <summary>T-34 #3: Spotify is asked far less often than every 2 s, yet a track change is still caught.</summary>
public class SpotifyPollPlannerTests
{
    private static readonly TimeSpan Floor = TimeSpan.FromSeconds(2);

    private static TimeSpan Plan(SpotifyPollOutcome outcome, bool playing = false, long progress = 0, long duration = 0, TimeSpan limited = default)
        => SpotifyPollPlanner.Next(Floor, outcome, playing, progress, duration, limited);

    [Fact]
    public void ALongTrackIsCheckedAtMostEveryFifteenSeconds()
    {
        var wait = Plan(SpotifyPollOutcome.Playback, playing: true, progress: 30_000, duration: 240_000);

        Assert.Equal(SpotifyPollPlanner.PlayingMax, wait);
    }

    [Fact]
    public void TheNextPollComesJustAfterTheTrackEnds_SoAChangeIsSeenAtOnce()
    {
        var wait = Plan(SpotifyPollOutcome.Playback, playing: true, progress: 236_000, duration: 240_000);

        Assert.Equal(TimeSpan.FromSeconds(5), wait);   // 4 s left + 1 s margin
    }

    [Fact]
    public void ATrackAboutToEndStillRespectsTheUsersFloor()
    {
        var wait = Plan(SpotifyPollOutcome.Playback, playing: true, progress: 239_900, duration: 240_000);

        Assert.Equal(Floor, wait);
    }

    [Theory]
    [InlineData(-1, 240_000)]
    [InlineData(250_000, 240_000)]
    [InlineData(1000, 0)]
    public void UnknownOrNonsenseProgress_FallsBackToTheNormalWait(long progress, long duration)
    {
        Assert.Equal(SpotifyPollPlanner.PlayingMax, Plan(SpotifyPollOutcome.Playback, playing: true, progress, duration));
    }

    [Fact]
    public void APausedTrack_IsCheckedEveryThirtySeconds()
    {
        Assert.Equal(SpotifyPollPlanner.IdleWait, Plan(SpotifyPollOutcome.Playback, playing: false, progress: 1000, duration: 200_000));
    }

    [Fact]
    public void NothingPlaying_IsCheckedEveryThirtySeconds()
    {
        Assert.Equal(SpotifyPollPlanner.IdleWait, Plan(SpotifyPollOutcome.NoPlayback));
    }

    [Fact]
    public void AFailedRequest_IsRetriedAfterTenSeconds()
    {
        Assert.Equal(SpotifyPollPlanner.FailureWait, Plan(SpotifyPollOutcome.Failed));
    }

    [Fact]
    public void WhenSpotifyAsksUsToWait_WeWaitAtLeastThatLong()
    {
        var wait = Plan(SpotifyPollOutcome.Failed, limited: TimeSpan.FromSeconds(90));

        Assert.Equal(TimeSpan.FromSeconds(90), wait);
    }

    [Fact]
    public void ARateLimitShorterThanTheNormalWaitChangesNothing()
    {
        var wait = Plan(SpotifyPollOutcome.Playback, playing: true, progress: 0, duration: 240_000, limited: TimeSpan.FromSeconds(3));

        Assert.Equal(SpotifyPollPlanner.PlayingMax, wait);
    }

    [Fact]
    public void TheFloorIsNeverBelowOneSecond()
    {
        var wait = SpotifyPollPlanner.Next(TimeSpan.Zero, SpotifyPollOutcome.Playback, true, 239_999, 240_000);

        Assert.Equal(TimeSpan.FromMilliseconds(1001), wait);   // 1 ms left + 1 s margin, and a zero floor is raised to 1 s
        Assert.True(wait >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AnHourOfListeningNeedsFarFewerRequestsThanEvery2Seconds()
    {
        // an endless 4-minute-track playlist: one poll per 15 s plus one per track end
        double perHour = 3600.0 / SpotifyPollPlanner.PlayingMax.TotalSeconds;

        Assert.True(perHour <= 240, $"{perHour} requests an hour");
        Assert.True(perHour < 1800 / 5.0, "at least five times fewer than the old fixed 2 s poll (1 800 an hour)");
    }
}

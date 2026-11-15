using MasterHub.Core.Devices;

namespace MasterHub.Core.Tests;

/// <summary>T-34 #13: "Connecting…" for the first seconds instead of a red "Disconnected"; a lost deck is still reported at once.</summary>
public class DeckLinkStatusTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        public Func<DateTimeOffset> Read => () => Now;
    }

    [Fact]
    public void RightAfterStart_AMissingDeckIsStillBeingConnected()
    {
        var clock = new Clock();
        var link = new DeckLinkStatus(now: clock.Read);

        Assert.Equal(DeckLinkPhase.Connecting, link.Evaluate(deckConnected: false));
        clock.Now += TimeSpan.FromSeconds(7.9);
        Assert.Equal(DeckLinkPhase.Connecting, link.Evaluate(false));
    }

    [Fact]
    public void AfterTheGraceTime_AMissingDeckIsReportedAsDisconnected()
    {
        var clock = new Clock();
        var link = new DeckLinkStatus(now: clock.Read);

        clock.Now += DeckLinkStatus.DefaultGrace;

        Assert.Equal(DeckLinkPhase.Disconnected, link.Evaluate(false));
    }

    [Fact]
    public void ADeckThatAnswers_IsConnectedAtOnce()
    {
        var link = new DeckLinkStatus(now: new Clock().Read);

        Assert.Equal(DeckLinkPhase.Connected, link.Evaluate(true));
    }

    [Fact]
    public void ADeckThatWasSeenAndGoesAway_IsDisconnectedImmediately_NotConnecting()
    {
        var clock = new Clock();
        var link = new DeckLinkStatus(now: clock.Read);
        link.Evaluate(true);
        clock.Now += TimeSpan.FromSeconds(1);

        Assert.Equal(DeckLinkPhase.Disconnected, link.Evaluate(false));
    }

    [Fact]
    public void ADeckThatComesBackAfterwards_IsConnectedAgain()
    {
        var clock = new Clock();
        var link = new DeckLinkStatus(now: clock.Read);
        link.Evaluate(true);
        link.Evaluate(false);

        Assert.Equal(DeckLinkPhase.Connected, link.Evaluate(true));
    }

    [Fact]
    public void TheGraceTimeCanBeChanged()
    {
        var clock = new Clock();
        var link = new DeckLinkStatus(TimeSpan.FromSeconds(2), clock.Read);

        clock.Now += TimeSpan.FromSeconds(3);

        Assert.Equal(DeckLinkPhase.Disconnected, link.Evaluate(false));
        Assert.Equal(TimeSpan.FromSeconds(2), link.Grace);
    }
}

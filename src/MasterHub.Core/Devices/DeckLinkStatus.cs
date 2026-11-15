using System;

namespace MasterHub.Core.Devices;

/// <summary>What the title bar says about the deck.</summary>
public enum DeckLinkPhase
{
    /// <summary>The app has just started and the deck has not answered yet: it normally does within a few seconds.</summary>
    Connecting,
    Connected,
    /// <summary>No deck, or it went away.</summary>
    Disconnected
}

/// <summary>
/// T-34 #13: for the first seconds after start the deck is still being found (a restart takes it back in about 6 s), so the bar says
/// "Connecting…" instead of a red "Disconnected". Once a deck has been seen, or the grace time is over, a missing deck is reported at once.
/// Pure and clock-injected, so it is easy to test.
/// </summary>
public sealed class DeckLinkStatus
{
    /// <summary>How long a deck that has not answered yet is still "being connected".</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(8);

    private readonly Func<DateTimeOffset> _now;
    private readonly DateTimeOffset _startedAt;
    private bool _seenConnected;

    public DeckLinkStatus(TimeSpan? grace = null, Func<DateTimeOffset>? now = null)
    {
        Grace = grace ?? DefaultGrace;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _startedAt = _now();
    }

    public TimeSpan Grace { get; }

    /// <summary>The phase for the current state of the deck; call it whenever the deck list changes and when the grace time ends.</summary>
    public DeckLinkPhase Evaluate(bool deckConnected)
    {
        if (deckConnected)
        {
            _seenConnected = true;
            return DeckLinkPhase.Connected;
        }

        // a deck that was there and went away is reported at once: waiting would hide a real problem
        if (_seenConnected) return DeckLinkPhase.Disconnected;

        return _now() - _startedAt < Grace ? DeckLinkPhase.Connecting : DeckLinkPhase.Disconnected;
    }
}

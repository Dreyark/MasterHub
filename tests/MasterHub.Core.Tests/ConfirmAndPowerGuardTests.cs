using MasterHub.Core.Actions;
using MasterHub.Core.Dialogs;

namespace MasterHub.Core.Tests;

/// <summary>T-03: awaitable confirmations and hold-to-confirm for destructive power keys.</summary>
public class ConfirmAndPowerGuardTests
{
    private static ConfirmRequest Req(string title = "Delete profile?") =>
        new(title, "message", "Delete profile", "Cancel");

    // ── IConfirmDialogService ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Accept_ResolvesTrue_AndClearsCurrent()
    {
        var svc = new ConfirmDialogService();
        var task = svc.ConfirmAsync(Req());

        Assert.Equal("Delete profile?", svc.Current?.Title);
        svc.Accept();

        Assert.True(await task);
        Assert.Null(svc.Current);
    }

    [Fact]
    public async Task Cancel_ResolvesFalse()
    {
        var svc = new ConfirmDialogService();
        var task = svc.ConfirmAsync(Req());
        svc.Cancel();
        Assert.False(await task);
    }

    [Fact]
    public async Task Timeout_ResolvesFalse_UsingInjectedDelay()
    {
        var gate = new TaskCompletionSource();
        var svc = new ConfirmDialogService((_, token) => gate.Task.WaitAsync(token));
        var task = svc.ConfirmAsync(Req(), TimeSpan.FromSeconds(30));

        Assert.NotNull(svc.Current);
        gate.SetResult(); // the "clock" reaches the timeout

        Assert.False(await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(svc.Current);
    }

    [Fact]
    public async Task AnsweringInTime_StopsTheTimeout()
    {
        var svc = new ConfirmDialogService((_, token) => Task.Delay(Timeout.Infinite, token));
        var task = svc.ConfirmAsync(Req(), TimeSpan.FromSeconds(30));
        svc.Accept();
        Assert.True(await task);
    }

    [Fact]
    public async Task CancellationToken_ResolvesFalse()
    {
        var svc = new ConfirmDialogService();
        using var cts = new CancellationTokenSource();
        var task = svc.ConfirmAsync(Req(), cancellationToken: cts.Token);
        cts.Cancel();

        Assert.False(await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(svc.Current);
    }

    [Fact]
    public async Task Requests_AreShownOneAtATime_InOrder()
    {
        var svc = new ConfirmDialogService();
        var first = svc.ConfirmAsync(Req("first"));
        var second = svc.ConfirmAsync(Req("second"));

        Assert.Equal("first", svc.Current?.Title);
        svc.Accept();
        Assert.True(await first);

        Assert.Equal("second", svc.Current?.Title);
        svc.Cancel();
        Assert.False(await second);
        Assert.Null(svc.Current);
    }

    [Fact]
    public void CurrentChanged_IsRaised_OnShowAndOnAnswer()
    {
        var svc = new ConfirmDialogService();
        int changes = 0;
        svc.CurrentChanged += () => changes++;

        _ = svc.ConfirmAsync(Req());
        svc.Accept();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void AcceptWithoutRequest_IsHarmless()
    {
        var svc = new ConfirmDialogService();
        svc.Accept();
        svc.Cancel();
        Assert.Null(svc.Current);
    }

    // ── power-action guard ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DeckActionType.SystemShutdown, true)]
    [InlineData(DeckActionType.SystemRestart, true)]
    [InlineData(DeckActionType.SystemSleep, true)]
    [InlineData(DeckActionType.SystemLockScreen, false)]
    [InlineData(DeckActionType.MediaPlayPause, false)]
    [InlineData(DeckActionType.None, false)]
    public void OnlyShutdownRestartSleep_AreDestructive(DeckActionType action, bool expected)
    {
        Assert.Equal(expected, PowerActionGuard.IsDestructive(action));
    }

    [Fact]
    public void DestructiveKey_IsForcedToHoldForOneAndAHalfSeconds()
    {
        var (mode, hold) = PowerActionGuard.Resolve(DeckActionType.SystemShutdown, KeyPressMode.OnPress, skipConfirmation: false);
        Assert.Equal(KeyPressMode.OnHold, mode);
        Assert.Equal(1500, hold);
    }

    [Fact]
    public void OptOut_KeepsTheConfiguredMode()
    {
        var (mode, hold) = PowerActionGuard.Resolve(DeckActionType.SystemShutdown, KeyPressMode.OnPress, skipConfirmation: true);
        Assert.Equal(KeyPressMode.OnPress, mode);
        Assert.Null(hold);
    }

    [Fact]
    public void OtherKeys_AreUntouched()
    {
        var (mode, hold) = PowerActionGuard.Resolve(DeckActionType.MediaNext, KeyPressMode.OnDoublePress, skipConfirmation: false);
        Assert.Equal(KeyPressMode.OnDoublePress, mode);
        Assert.Null(hold);
    }

    [Fact]
    public async Task Engine_HoldOverride_FiresOnlyAfterTheHold_AndReleaseCancels()
    {
        using var engine = new KeyInteractionEngine { HoldDurationMs = 10_000 };
        var fired = new List<KeyPressMode>();
        engine.KeyTriggered += (_, m) => fired.Add(m);

        // pressed and released before the (override) hold elapses -> nothing happens (a wide margin: a busy CI runner can stall a 40 ms delay for hundreds of ms)
        engine.HandleKeyEvent(0, true, KeyPressMode.OnHold, holdDurationMs: 3000);
        await Task.Delay(40);
        engine.HandleKeyEvent(0, false, KeyPressMode.OnHold, holdDurationMs: 3000);
        await Task.Delay(300);
        Assert.Empty(fired);

        // held through the override -> fires once, even though the global hold time is 10 s
        engine.HandleKeyEvent(1, true, KeyPressMode.OnHold, holdDurationMs: 60);
        await Task.Delay(400);
        engine.HandleKeyEvent(1, false, KeyPressMode.OnHold, holdDurationMs: 60);
        Assert.Equal(new[] { KeyPressMode.OnHold }, fired);
    }
}

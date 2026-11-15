using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Core.Audio;
using MasterHub.Core.Looks;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Status;

namespace MasterHub.UiTests;

/// <summary>T-25: the keys' "When" rules and label tokens read the status board.</summary>
public class StatusUiTests
{
    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private static LookSignals Signals(IStatusBoard? board) =>
        new(F<ISystemMonitorService>(), F<INowPlayingService>(), null, () => null, () => new DateTime(2026, 11, 14, 12, 0, 0), board);

    [Fact]
    public void TheSignals_CountWhatTheBoardHolds()
    {
        var board = new StatusBoard();
        var tracker = new AgentTracker(board);
        tracker.Apply(new AgentEvent("a", "Alpha", "PermissionRequest", "Bash"));
        tracker.Apply(new AgentEvent("b", "Beta", "PreToolUse", null));
        tracker.Apply(new AgentEvent("c", "Gamma", "Stop", null));

        var signals = Signals(board);

        Assert.Equal(1, signals.StatusWaiting);
        Assert.Equal(1, signals.StatusWorking);
        Assert.Equal(1, signals.StatusDone);
        Assert.Equal(0, signals.StatusError);
        Assert.Equal("waiting", signals.StatusTop);      // the most urgent one
        Assert.Equal("Alpha", signals.StatusTopLabel);   // and who it is
    }

    [Fact]
    public void AnIdleSession_IsNotNewsForAKey()
    {
        var board = new StatusBoard();
        new AgentTracker(board).Apply(new AgentEvent("a", "Alpha", "SessionStart", null));

        var signals = Signals(board);

        Assert.Null(signals.StatusTop);
        Assert.Equal("", LabelTokens.Substitute("{status}", signals));
    }

    [Fact]
    public void WithoutABoard_NothingReports()
    {
        var signals = Signals(null);

        Assert.Equal(0, signals.StatusWaiting);
        Assert.Null(signals.StatusTop);
    }

    [Fact]
    public void ARuleThatWatchesTheBoard_FollowsItLive()
    {
        var board = new StatusBoard();
        var tracker = new AgentTracker(board);
        var signals = Signals(board);
        var rules = new[] { new KeyLookRule { When = new KeyLookCondition { Kind = LookConditionKind.StatusWaiting }, BackgroundColorHex = "#ff8c00", Title = "{status_name}" } };

        Assert.True(KeyLookEvaluator.Evaluate(rules, signals, new KeyLookState()).IsNone);

        tracker.Apply(new AgentEvent("a", "MasterHub", "Notification", null));
        var look = KeyLookEvaluator.Evaluate(rules, signals, new KeyLookState());
        Assert.Equal("#ff8c00", look.Background);
        Assert.Equal("MasterHub", look.Title);

        tracker.Apply(new AgentEvent("a", "MasterHub", "Stop", null));
        Assert.True(KeyLookEvaluator.Evaluate(rules, signals, new KeyLookState()).IsNone);
    }

    [Fact]
    public void TheRuleEditor_OffersTheFourStatusConditions()
    {
        var kinds = KeyItemViewModel.LookConditionOptions.Select(o => o.Kind).ToList();

        Assert.Contains(LookConditionKind.StatusWaiting, kinds);
        Assert.Contains(LookConditionKind.StatusWorking, kinds);
        Assert.Contains(LookConditionKind.StatusDone, kinds);
        Assert.Contains(LookConditionKind.StatusError, kinds);
    }
}

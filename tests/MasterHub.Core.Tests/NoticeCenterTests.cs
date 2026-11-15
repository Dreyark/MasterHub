using System;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Notices;
using MasterHub.Core.SystemControl;
using Xunit;

namespace MasterHub.Core.Tests;

/// <summary>T-34 #8 (banners in the window) and #12 (elevated-app warning).</summary>
public class NoticeCenterTests
{
    private DateTime _now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private NoticeCenter Create() => new(() => _now);

    [Fact]
    public void TheSameThingAgain_RefreshesOneBanner_InsteadOfAddingAnother()
    {
        var c = Create();
        int changes = 0;
        c.Changed += () => changes++;

        var first = c.Post(NoticeLevel.Warning, "OBS is offline", "obs");
        _now += TimeSpan.FromSeconds(5);
        var second = c.Post(NoticeLevel.Error, "OBS is still offline", "obs");

        var only = Assert.Single(c.Current);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("OBS is still offline", only.Text);
        Assert.Equal(NoticeLevel.Error, only.Level);   // the more serious level wins
        Assert.Equal(2, changes);
    }

    [Fact]
    public void AfterTheMergeTime_ItIsANewBanner()
    {
        var c = Create();
        var first = c.Post(NoticeLevel.Error, "x", "k");
        _now += NoticeCenter.MergeWindow + TimeSpan.FromSeconds(1);
        var second = c.Post(NoticeLevel.Error, "x", "k");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Single(c.Current);
    }

    [Fact]
    public void InformationAndWarnings_GoAway_ErrorsStayUntilDismissed()
    {
        var c = Create();
        c.Post(NoticeLevel.Info, "saved");
        c.Post(NoticeLevel.Warning, "careful");
        var error = c.Post(NoticeLevel.Error, "broken");

        _now += NoticeCenter.InfoLifetime;
        c.Prune();
        Assert.Equal(2, c.Current.Count);
        _now += NoticeCenter.WarningLifetime;
        c.Prune();
        Assert.Equal(new[] { "broken" }, c.Current.Select(n => n.Text));

        c.Dismiss(error.Id);
        Assert.Empty(c.Current);
    }

    [Fact]
    public void OnlyAFewBanners_AreShown_TheOldestGoesFirst()
    {
        var c = Create();
        for (int i = 0; i < NoticeCenter.MaxShown + 2; i++) c.Post(NoticeLevel.Error, "e" + i);

        Assert.Equal(NoticeCenter.MaxShown, c.Current.Count);
        Assert.Equal("e2", c.Current[0].Text);
        c.DismissAll();
        Assert.Empty(c.Current);
    }

    [Fact]
    public void VeryLongTexts_AreShortened()
    {
        var notice = Create().Post(NoticeLevel.Error, new string('x', 1000));
        Assert.True(notice.Text.Length <= 301);
    }

    [Theory]
    [InlineData(false, true, true)]    // we are normal, the target is elevated: presses are dropped
    [InlineData(true, true, false)]    // both elevated: fine
    [InlineData(false, false, false)]
    public void ElevatedTargets_DropOurKeyPresses_OnlyWhenWeAreNotElevated(bool self, bool target, bool dropped)
    {
        Assert.Equal(dropped, ElevatedInputGuard.InputWillBeDropped(self, new ForegroundTarget(1, "game", target)));
    }

    [Fact]
    public void AnUnknownForeground_IsNotWarnedAbout()
    {
        Assert.False(ElevatedInputGuard.InputWillBeDropped(false, null));
        Assert.False(ElevatedInputGuard.InputWillBeDropped(false, new ForegroundTarget(1, "x", null)));
    }

    [Fact]
    public void OnlyKeystrokeActions_AreChecked()
    {
        Assert.True(ElevatedInputGuard.SendsKeystrokes(DeckActionType.Hotkey));
        Assert.True(ElevatedInputGuard.SendsKeystrokes(DeckActionType.TextPaste));
        Assert.True(ElevatedInputGuard.SendsKeystrokes(DeckActionType.MacroPlay));
        Assert.False(ElevatedInputGuard.SendsKeystrokes(DeckActionType.MediaNext));
        Assert.False(ElevatedInputGuard.SendsKeystrokes(DeckActionType.ObsSceneSwitch));
    }

    [Fact]
    public void TheWindowsProbe_AnswersWithoutThrowing()
    {
        var probe = new WindowsElevationProbe();
        _ = probe.IsSelfElevated;
        _ = probe.GetForeground();   // may be null on a desktop-less test agent
    }
}

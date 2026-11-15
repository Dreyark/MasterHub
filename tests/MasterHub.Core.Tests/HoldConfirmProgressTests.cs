using System.IO;
using MasterHub.Core.Actions;
using MasterHub.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace MasterHub.Core.Tests;

/// <summary>T-03: the bar that fills on a power key while it is held.</summary>
public class HoldConfirmProgressTests
{
    private long _now = 1_000;
    private HoldConfirmProgress Create() => new(() => _now);

    [Fact]
    public void TheBar_FillsWithTheTimeHeld_InSteps()
    {
        var p = Create();
        p.Start(3, 1500);

        Assert.Equal(0, p.Fraction(3));
        _now += 750;
        Assert.Equal(0.5, p.Fraction(3), 3);
        _now += 70;   // 820 ms = 0.546 → rounded down to the step 0.5
        Assert.Equal(0.5, p.Fraction(3), 3);
        _now += 5_000;
        Assert.Equal(1.0, p.Fraction(3));   // never more than full
    }

    [Fact]
    public void Release_StopsTheBar_AndTellsTheKeyToRedraw()
    {
        var p = Create();
        p.Start(1, 1500);

        Assert.True(p.IsRunning(1));
        Assert.True(p.Stop(1));
        Assert.False(p.Stop(1));   // a second stop: nothing to redraw
        Assert.False(p.IsAnyRunning);
        Assert.Equal(0, p.Fraction(1));
    }

    [Fact]
    public void SeveralKeys_HaveTheirOwnBars()
    {
        var p = Create();
        p.Start(0, 1000);
        _now += 500;
        p.Start(4, 1000);

        Assert.Equal(new[] { 0, 4 }, p.Running);
        Assert.Equal(0.5, p.Fraction(0), 3);
        Assert.Equal(0, p.Fraction(4));
    }

    [Fact]
    public void ANonPositiveDuration_StartsNothing()
    {
        var p = Create();
        p.Start(2, 0);
        Assert.False(p.IsRunning(2));
    }

    [Theory]
    [InlineData(DeckActionType.SystemShutdown, false, false, null, PowerActionGuard.HoldToConfirmMs)]
    [InlineData(DeckActionType.SystemSleep, false, false, null, PowerActionGuard.HoldToConfirmMs)]
    [InlineData(DeckActionType.SystemShutdown, true, false, null, null)]        // the key opted out: one press
    [InlineData(DeckActionType.MediaNext, false, false, null, null)]            // not a power key
    [InlineData(DeckActionType.SystemShutdown, false, true, null, null)]        // with gestures only a destructive Hold waits
    [InlineData(DeckActionType.MediaNext, false, true, 1500, 1500)]             // ... like this one
    public void DurationFor_OnlyPowerKeys_NeedTheHold(DeckActionType action, bool skip, bool gestures, int? gestureHold, int? expected)
    {
        Assert.Equal(expected, HoldConfirmProgress.DurationFor(action, skip, gestures, gestureHold));
    }

    private static byte[] GreyKey()
    {
        using var surface = SKSurface.Create(new SKImageInfo(72, 72));
        surface.Canvas.Clear(new SKColor(0x40, 0x40, 0x40));
        using var image = surface.Snapshot();
        using var data = JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }

    private static bool IsOrange(SKColor c) => c.Red > 200 && c.Green is > 100 and < 170 && c.Blue < 80;

    [Fact]
    public void TheRenderer_DrawsAnOrangeBar_AtTheBottom_AsLongAsTheFraction()
    {
        byte[] key = GreyKey();

        using var half = SKBitmap.Decode(ButtonImageRenderer.WithHoldProgress(key, 0.5));
        int y = 72 - 4 - 5;   // inside the bar (margin ≈ 4 px, height ≈ 11 px)
        Assert.True(IsOrange(half.GetPixel(15, y)), "left part should be filled: " + half.GetPixel(15, y));
        Assert.False(IsOrange(half.GetPixel(60, y)), "right part should be empty: " + half.GetPixel(60, y));
        Assert.False(IsOrange(half.GetPixel(36, 20)), "the top of the key is not the bar");

        using var full = SKBitmap.Decode(ButtonImageRenderer.WithHoldProgress(key, 1));
        Assert.True(IsOrange(full.GetPixel(60, y)));

        using var none = SKBitmap.Decode(ButtonImageRenderer.WithHoldProgress(key, 0));
        Assert.False(IsOrange(none.GetPixel(15, y)));
    }

    [Fact]
    public void TheRenderer_LeavesABrokenPicture_AsItIs()
    {
        byte[] junk = { 1, 2, 3 };
        Assert.Same(junk, ButtonImageRenderer.WithHoldProgress(junk, 0.5));
        Assert.Empty(ButtonImageRenderer.WithHoldProgress(System.Array.Empty<byte>(), 0.5));
    }
}

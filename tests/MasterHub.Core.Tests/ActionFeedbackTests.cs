using MasterHub.Core.Actions;
using MasterHub.Core.Rendering;
using SkiaSharp;

namespace MasterHub.Core.Tests;

/// <summary>T-07: action results and the red corner of a key whose last press failed.</summary>
public class ActionFeedbackTests
{
    [Fact]
    public void Results_MapBoolAndCarryTheReason()
    {
        Assert.Equal(ActionOutcome.Ok, ActionResult.FromBool(true, "x").Outcome);

        var failed = ActionResult.FromBool(false, "OBS is offline");
        Assert.True(failed.IsFailure);
        Assert.Equal("OBS is offline", failed.Reason);

        Assert.False(ActionResult.NotApplicable.IsFailure);
    }

    private static byte[] Blank() => ButtonImageRenderer.GetBlankJpeg();

    [Fact]
    public void FailureCorner_IsRedInTheTopRightCorner_AndLeavesTheRestAlone()
    {
        byte[] plain = Blank();
        byte[] marked = ButtonImageRenderer.WithFailureCorner(plain);

        using var before = SKBitmap.Decode(plain);
        using var after = SKBitmap.Decode(marked);
        Assert.Equal(before.Width, after.Width);

        var corner = after.GetPixel(after.Width - 4, 4);          // inside the triangle (top right)
        Assert.True(corner.Red > 180 && corner.Green < 90 && corner.Blue < 90, $"corner was {corner}");

        var farAway = after.GetPixel(4, after.Height - 4);        // bottom left: untouched
        var original = before.GetPixel(4, before.Height - 4);
        Assert.InRange(Math.Abs(farAway.Red - original.Red), 0, 8);
        Assert.InRange(Math.Abs(farAway.Green - original.Green), 0, 8);

        var outsideTriangle = after.GetPixel(after.Width - (int)(after.Width * 0.34f) + 2, after.Height / 2);
        Assert.True(outsideTriangle.Red < 150 || outsideTriangle.Green > 60, "the corner must not spread over the key");
    }

    [Fact]
    public void FailureCorner_IsCached_ForTheSamePicture()
    {
        byte[] plain = Blank();
        Assert.Same(ButtonImageRenderer.WithFailureCorner(plain), ButtonImageRenderer.WithFailureCorner((byte[])plain.Clone()));
        Assert.Empty(ButtonImageRenderer.WithFailureCorner(Array.Empty<byte>()));
    }
}

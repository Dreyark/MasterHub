using System;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// The shape a key is drawn in. A key with a picture fills the whole LCD (no black margin, no rounded clip, no outline), so the
/// picture reaches every edge of the key like in the official app; a key made of colour and text keeps the rounded card with its
/// thin outline. One place decides this, so every key renderer draws the same shape.
/// </summary>
public sealed class KeyFrame : IDisposable
{
    private const float MarginPx = 2f;
    private const float CornerPx = 7f;

    private KeyFrame(bool fullBleed, int width, int height, float scale)
    {
        IsFullBleed = fullBleed;
        Bounds = new SKRect(0, 0, width, height);
        float m = fullBleed ? 0 : MarginPx * scale;
        float r = fullBleed ? 0 : CornerPx * scale;
        ContentRect = new SKRect(m, m, width - m, height - m);
        Shape = new SKRoundRect(ContentRect, r, r);
    }

    /// <summary>The whole key, edge to edge: for keys that show a picture.</summary>
    public static KeyFrame FullBleed(int width, int height) => new(true, width, height, 1f);

    /// <summary>The rounded card with a 2 px margin (scaled): for keys made of colour and text.</summary>
    public static KeyFrame Card(int width, int height, float scale) => new(false, width, height, scale);

    /// <summary>Picks <see cref="FullBleed"/> when the key shows a picture, otherwise <see cref="Card"/>.</summary>
    public static KeyFrame For(bool hasPicture, int width, int height, float scale)
        => hasPicture ? FullBleed(width, height) : Card(width, height, scale);

    public bool IsFullBleed { get; }

    /// <summary>The whole drawing surface.</summary>
    public SKRect Bounds { get; }

    /// <summary>Where the background and the picture go.</summary>
    public SKRect ContentRect { get; }

    /// <summary>The outline of the content (a plain rectangle when full bleed).</summary>
    public SKRoundRect Shape { get; }

    /// <summary>Limits drawing to the key's shape. Full-bleed keys need no clip.</summary>
    public void Clip(SKCanvas canvas)
    {
        if (!IsFullBleed) canvas.ClipRoundRect(Shape, SKClipOperation.Intersect, true);
    }

    /// <summary>
    /// Whether an outline is drawn on top. A card always has one. A full-bleed picture never has one at rest (that was the
    /// "frame" around pictures) nor while pressed (it gets a tint); only a picture key that is toggled on without its own
    /// second picture gets a ring, otherwise its state could not be seen.
    /// </summary>
    public bool ShouldDrawOutline(bool isPressed, bool isActiveState, bool showsActivePicture)
        => !IsFullBleed || (isActiveState && !isPressed && !showsActivePicture);

    /// <summary>The rectangle to stroke with <paramref name="strokeWidth"/> so the whole line stays inside the key.</summary>
    public SKRoundRect OutlineFor(float strokeWidth)
    {
        if (!IsFullBleed) return new SKRoundRect(Shape.Rect, Shape.Radii[0].X, Shape.Radii[0].Y);
        var rect = ContentRect;
        rect.Inflate(-strokeWidth / 2f, -strokeWidth / 2f);
        return new SKRoundRect(rect, 0, 0);
    }

    public void Dispose() => Shape.Dispose();
}

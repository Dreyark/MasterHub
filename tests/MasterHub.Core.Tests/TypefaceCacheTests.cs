using MasterHub.Core.Actions;
using MasterHub.Core.Rendering;
using SkiaSharp;

namespace MasterHub.Core.Tests;

/// <summary>Cached typefaces are shared by every key: drawing a key must never dispose them.</summary>
public class TypefaceCacheTests
{
    [Fact]
    public void Drawing_text_keys_does_not_dispose_the_cached_typefaces()
    {
        using var style = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var before = ButtonImageRenderer.GetOrCreateTypeface("Segoe UI", style);

        ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 1, "Hello", "sub", null, null, null);
        ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 2, "😀", "", null, null, null);
        ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 3, "Again", "", null, null, null);

        var after = ButtonImageRenderer.GetOrCreateTypeface("Segoe UI", style);
        Assert.Same(before, after);
        Assert.NotEqual(IntPtr.Zero, after.Handle);
    }

    [Fact]
    public void Text_the_chosen_font_can_draw_keeps_that_font()
    {
        using var style = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var primary = ButtonImageRenderer.GetOrCreateTypeface("Segoe UI", style);

        Assert.Same(primary, ButtonImageRenderer.WithGlyphFallback(primary, "Hello 123", style));
        Assert.Same(primary, ButtonImageRenderer.WithGlyphFallback(primary, "", style));
        Assert.Same(primary, ButtonImageRenderer.WithGlyphFallback(primary, null, style));
    }

    [Fact]
    public void A_character_the_font_lacks_gets_a_system_font_that_has_it()
    {
        using var style = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var primary = ButtonImageRenderer.GetOrCreateTypeface("Segoe UI", style);
        Assert.False(ButtonImageRenderer.HasGlyph(primary, '日'), "test premise: Segoe UI has no CJK glyphs");

        var chosen = ButtonImageRenderer.WithGlyphFallback(primary, "日本語", style);

        // a PC without any CJK font keeps the primary font (nothing better exists); otherwise the fallback must contain the glyph
        Assert.True(ReferenceEquals(chosen, primary) || ButtonImageRenderer.HasGlyph(chosen, '日'));
    }

    [Fact]
    public void A_broken_surrogate_does_not_throw()
    {
        using var style = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var primary = ButtonImageRenderer.GetOrCreateTypeface("Segoe UI", style);

        var ex = Record.Exception(() => ButtonImageRenderer.WithGlyphFallback(primary, "a\uD83Db", style));

        Assert.Null(ex);
    }

    [Fact]
    public void A_japanese_key_title_is_drawn_not_left_as_empty_boxes()
    {
        byte[] withText = ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 1, "日本語", "", "#000000", "#FFFFFF", null);
        byte[] blank = ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 1, "", "", "#000000", "#FFFFFF", null);

        using var a = SKBitmap.Decode(withText);
        int lit = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y).Red > 128) lit++;
        Assert.NotEqual(withText, blank);
        Assert.True(lit > 40, $"only {lit} bright pixels: the characters were not drawn");
    }
}

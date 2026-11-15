using System;
using System.Collections.Generic;
using System.Threading;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// The wallpaper of the whole keypad, as one picture (a still image, a GIF or a video) with what the keys add on top of it: their text and icons.
/// It lays the overlays over the wallpaper, sends the result to the keypad as <b>one</b> 480 × 272 picture and hands each key its own 72 × 72 piece
/// (for the on-screen preview). Because the keys' text is part of the sent picture, a key with a label no longer cuts a hole in the wallpaper,
/// and a moving wallpaper keeps the labels on it.
/// </summary>
public sealed class PanoramaDeck : IDisposable
{
    public const int Keys = 15;

    private readonly Action<byte[]> _sendPanel;
    private readonly Action<int, byte[]>? _keyPreview;
    private readonly bool _useTimer;
    private readonly object _lock = new();
    private readonly byte[]?[] _overlayPng = new byte[Keys][];
    private readonly SKBitmap?[] _overlay = new SKBitmap?[Keys];
    private IReadOnlyList<byte[]> _frames = Array.Empty<byte[]>();
    private int _frame;
    private int _intervalMs = 100;
    private Timer? _timer;
    private Timer? _refreshTimer;
    private bool _paused;
    private bool _disposed;

    /// <param name="sendPanel">Receives the finished 480 × 272 JPEG for the keypad.</param>
    /// <param name="keyPreview">Receives <c>(key, 72 × 72 JPEG)</c> for each key of every picture composed (the on-screen preview). Optional.</param>
    /// <param name="useTimer">Tests turn the timers off and call <see cref="Tick"/> and <see cref="Refresh"/> themselves.</param>
    public PanoramaDeck(Action<byte[]> sendPanel, Action<int, byte[]>? keyPreview = null, bool useTimer = true)
    {
        _sendPanel = sendPanel ?? throw new ArgumentNullException(nameof(sendPanel));
        _keyPreview = keyPreview;
        _useTimer = useTimer;
    }

    public bool HasSource
    {
        get { lock (_lock) return _frames.Count > 0; }
    }

    public bool IsAnimated
    {
        get { lock (_lock) return _frames.Count > 1; }
    }

    public int CurrentFrame
    {
        get { lock (_lock) return _frame; }
    }

    /// <summary>Where the top left corner of a key lies in the 480 × 272 panel.</summary>
    public static (int X, int Y) KeyOrigin(int key)
    {
        int col = Math.Clamp(key % 5, 0, 4);
        int row = Math.Clamp(key / 5, 0, 2);
        return (Math.Clamp(col * ButtonImageRenderer.KeyPitchX, 0, ButtonImageRenderer.PanelWidth - ButtonImageRenderer.ButtonWidth),
                Math.Clamp(row * ButtonImageRenderer.KeyPitchY, 0, ButtonImageRenderer.PanelHeight - ButtonImageRenderer.ButtonHeight));
    }

    /// <summary>Sets the wallpaper (480 × 272 JPEG frames). One frame = a still picture; more = it plays, looping, at <paramref name="fps"/>.</summary>
    public void SetSource(IReadOnlyList<byte[]> frames, int fps = 10)
    {
        lock (_lock)
        {
            if (_disposed) return;

            _timer?.Dispose();
            _timer = null;
            _frames = frames ?? Array.Empty<byte[]>();
            _frame = 0;
            _intervalMs = 1000 / Math.Clamp(fps, 1, 30);

            if (_frames.Count > 1 && _useTimer && !_paused) _timer = new Timer(_ => Tick(), null, _intervalMs, _intervalMs);
        }

        Refresh();
    }

    /// <summary>What a key adds on top of the wallpaper (a transparent 72 × 72 PNG), or <c>null</c> for nothing. Takes effect with the next picture.</summary>
    public void SetOverlay(int key, byte[]? png)
    {
        if ((uint)key >= Keys) return;

        lock (_lock)
        {
            if (_disposed) return;
            if (ReferenceEquals(_overlayPng[key], png)) return;

            _overlayPng[key] = png;
            _overlay[key]?.Dispose();
            _overlay[key] = null;
        }

        ScheduleRefresh();
    }

    /// <summary>Plays or pauses a moving wallpaper (standby, a locked PC) without losing the place.</summary>
    public void SetPaused(bool paused)
    {
        lock (_lock)
        {
            if (_disposed || _paused == paused) return;
            _paused = paused;
            if (_timer == null) return;
            _timer.Change(paused ? Timeout.Infinite : _intervalMs, paused ? Timeout.Infinite : _intervalMs);
        }
    }

    /// <summary>Removes the wallpaper and stops playing. The keys go back to their own pictures.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _refreshTimer?.Dispose();
            _refreshTimer = null;
            _frames = Array.Empty<byte[]>();
            _frame = 0;
        }
    }

    /// <summary>One step of a moving wallpaper: the next frame is composed and sent.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (_disposed || _paused || _frames.Count <= 1) return;
            _frame = (_frame + 1) % _frames.Count;
            ComposeAndSend();
        }
    }

    /// <summary>Composes the current frame with the overlays and sends it now.</summary>
    public void Refresh()
    {
        lock (_lock)
        {
            if (_disposed || _frames.Count == 0) return;
            ComposeAndSend();
        }
    }

    // Many overlays arrive together (a profile with 15 keys): one picture for all of them, not fifteen.
    private void ScheduleRefresh()
    {
        if (!_useTimer) return;

        lock (_lock)
        {
            if (_disposed || _frames.Count == 0) return;
            _refreshTimer?.Dispose();
            _refreshTimer = new Timer(_ => Refresh(), null, 40, Timeout.Infinite);
        }
    }

    /// <summary>The picture that would be sent for the current frame, without sending it (tests).</summary>
    internal byte[]? ComposeCurrent()
    {
        lock (_lock)
        {
            if (_frames.Count == 0) return null;
            using var panel = Compose();
            return panel == null ? null : Encode(panel, 92);
        }
    }

    private void ComposeAndSend()
    {
        using var panel = Compose();
        if (panel == null) return;

        byte[] jpeg = Encode(panel, 92);
        try { _sendPanel(jpeg); } catch { /* a failed send must not stop a moving wallpaper */ }

        if (_keyPreview == null) return;
        for (int key = 0; key < Keys; key++)
        {
            var (x, y) = KeyOrigin(key);
            using var piece = new SKBitmap(ButtonImageRenderer.ButtonWidth, ButtonImageRenderer.ButtonHeight);
            if (!panel.ExtractSubset(piece, new SKRectI(x, y, x + ButtonImageRenderer.ButtonWidth, y + ButtonImageRenderer.ButtonHeight))) continue;

            try { _keyPreview(key, Encode(piece, 95)); } catch { }
        }
    }

    /// <summary>The wallpaper frame with every overlay on top, as a 480 × 272 bitmap. Call with the lock held.</summary>
    private SKBitmap? Compose()
    {
        using var frame = SKBitmap.Decode(_frames[_frame]);
        if (frame == null) return null;

        var panel = new SKBitmap(ButtonImageRenderer.PanelWidth, ButtonImageRenderer.PanelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(panel);
        canvas.Clear(SKColors.Black);
        canvas.DrawBitmap(frame, new SKRect(0, 0, panel.Width, panel.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);

        for (int key = 0; key < Keys; key++)
        {
            var overlay = OverlayOf(key);
            if (overlay == null) continue;

            var (x, y) = KeyOrigin(key);
            canvas.DrawBitmap(overlay, new SKRect(x, y, x + overlay.Width, y + overlay.Height), new SKSamplingOptions(SKFilterMode.Nearest), null);
        }

        return panel;
    }

    private SKBitmap? OverlayOf(int key)
    {
        if (_overlay[key] != null) return _overlay[key];
        var png = _overlayPng[key];
        if (png == null) return null;

        return _overlay[key] = SKBitmap.Decode(png);
    }

    private static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = JpegEncoding.Encode(image, quality);
        return data.ToArray();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _refreshTimer?.Dispose();
            foreach (var bitmap in _overlay) bitmap?.Dispose();
            Array.Clear(_overlay);
        }
    }
}

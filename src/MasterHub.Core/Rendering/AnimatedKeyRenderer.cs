using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MasterHub.Core.Devices;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Silnik odtwarzania animowanych ikon GIF na klawiszach LCD (Etap 3.4).
/// Dekoduje klatki za pomocą SkiaSharp SKCodec, buforuje pre-renderowane JPEGi 72x72
/// i odświeża sprzętowe wyświetlacze w dedykowanym timerze per-klawisz.
/// </summary>
public sealed class AnimatedKeyRenderer : IAnimatedKeyRenderer
{
    private sealed class AnimationSession : IDisposable
    {
        public int KeyIndex { get; }
        public IReadOnlyList<byte[]> Frames { get; }
        public int FrameIntervalMs { get; }
        public int CurrentFrameIndex;
        private Timer? _timer;
        public bool IsDisposed { get; private set; }

        public AnimationSession(int keyIndex, IReadOnlyList<byte[]> frames, int fps, Action<int> onTick)
        {
            KeyIndex = keyIndex;
            Frames = frames;
            int clampedFps = Math.Clamp(fps, 1, 60);
            FrameIntervalMs = 1000 / clampedFps;

            _timer = new Timer(_ =>
            {
                if (!IsDisposed)
                {
                    onTick(KeyIndex);
                }
            }, null, FrameIntervalMs, FrameIntervalMs);
        }

        public void Dispose()
        {
            IsDisposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private readonly object _syncRoot = new();
    private readonly IKeyRenderer _keyRenderer;
    private readonly ConcurrentDictionary<int, AnimationSession> _activeSessions = new();
    private bool _disposed;

    public event Action<int, int>? FrameRendered;
    public event Action<int, int, byte[]>? FrameRenderedWithJpeg;

    public AnimatedKeyRenderer(IKeyRenderer keyRenderer)
    {
        _keyRenderer = keyRenderer ?? throw new ArgumentNullException(nameof(keyRenderer));
    }

    public AnimatedKeyRenderer(IMasterHubManager manager)
        : this(new KeyRenderer(manager))
    {
    }

    public void PlayGif(int keyIndex, string gifPath, int fps = 10)
    {
        PlayMedia(keyIndex, gifPath, fps);
    }

    public void PlayMedia(int keyIndex, string mediaPath, int fps = 10)
    {
        PlayMedia(keyIndex, mediaPath, fps, "Cover", 100, 0, 0);
    }

    public void PlayMedia(int keyIndex, string mediaPath, int fps, string fitMode, int scale, int offsetX, int offsetY)
    {
        if (keyIndex < 0 || keyIndex > 14) return;
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath)) return;

        StopAnimation(keyIndex);

        var frames = ExtractFrames(mediaPath, fitMode, scale, offsetX, offsetY);
        if (frames.Count == 0) return;

        // Natychmiast wyświetl klatkę 0
        DispatchFrame(keyIndex, frames[0], 0);

        if (frames.Count == 1)
        {
            // Pojedyncza klatka – brak potrzeby uruchamiania timera
            return;
        }

        lock (_syncRoot)
        {
            if (_disposed) return;

            var session = new AnimationSession(keyIndex, frames, fps, OnFrameTick);
            _activeSessions[keyIndex] = session;
        }
    }

    public void PlayFrames(int keyIndex, IReadOnlyList<byte[]> frames, int fps = 10)
    {
        if (keyIndex < 0 || keyIndex > 14 || frames == null || frames.Count == 0) return;

        StopAnimation(keyIndex);

        DispatchFrame(keyIndex, frames[0], 0);

        if (frames.Count == 1) return;

        lock (_syncRoot)
        {
            if (_disposed) return;

            var session = new AnimationSession(keyIndex, frames, fps, OnFrameTick);
            _activeSessions[keyIndex] = session;
        }
    }

    private void OnFrameTick(int keyIndex)
    {
        if (_disposed) return;

        if (_activeSessions.TryGetValue(keyIndex, out var session))
        {
            if (session.IsDisposed) return;

            int nextIndex;
            byte[] frameJpeg;

            lock (session)
            {
                if (session.IsDisposed) return;
                session.CurrentFrameIndex = (session.CurrentFrameIndex + 1) % session.Frames.Count;
                nextIndex = session.CurrentFrameIndex;
                frameJpeg = session.Frames[nextIndex];
            }

            if (!_activeSessions.ContainsKey(keyIndex) || session.IsDisposed) return;

            DispatchFrame(keyIndex, frameJpeg, nextIndex);
        }
    }

    private void DispatchFrame(int keyIndex, byte[] jpeg, int frameIndex)
    {
        if (_keyRenderer is KeyRenderer concrete)
        {
            concrete.SetKeyImageFromJpeg(keyIndex, jpeg);
        }
        else
        {
            using var bmp = SKBitmap.Decode(jpeg);
            if (bmp != null)
            {
                _keyRenderer.SetKeyImageFromBitmap(keyIndex, bmp);
            }
        }

        FrameRendered?.Invoke(keyIndex, frameIndex);
        FrameRenderedWithJpeg?.Invoke(keyIndex, frameIndex, jpeg);
    }

    public void StopAnimation(int keyIndex)
    {
        if (_activeSessions.TryRemove(keyIndex, out var session))
        {
            lock (session)
            {
                session.Dispose();
            }
        }
    }

    public void StopAllAnimations()
    {
        var keys = System.Linq.Enumerable.ToArray(_activeSessions.Keys);
        foreach (var keyIndex in keys)
        {
            StopAnimation(keyIndex);
        }
    }

    public bool IsAnimating(int keyIndex)
    {
        return _activeSessions.ContainsKey(keyIndex);
    }

    public int GetFrameCount(int keyIndex)
    {
        return _activeSessions.TryGetValue(keyIndex, out var session) ? session.Frames.Count : 0;
    }

    public static List<byte[]> ExtractFrames(
        string filePath,
        string fitMode = "Cover",
        int scale = 100,
        int offsetX = 0,
        int offsetY = 0)
    {
        return new List<byte[]>(MediaFrameExtractor.ExtractFrames(filePath, 120, fitMode, scale, offsetX, offsetY));
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                StopAllAnimations();
                _disposed = true;
            }
        }
    }
}

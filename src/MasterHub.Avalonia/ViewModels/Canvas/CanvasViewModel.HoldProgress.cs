using System;
using Avalonia.Threading;
using MasterHub.Core.Actions;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>
/// The hold-to-confirm bar of power keys (T-03): while a shutdown / restart / sleep key is held, a bar at its bottom fills
/// up in 1.5 s; releasing earlier cancels and the key is drawn again without the bar. The action itself still comes from
/// the key engine (its hold timer); this only shows the progress.
/// </summary>
public partial class CanvasViewModel
{
    private readonly HoldConfirmProgress _holdProgress = new();
    private DispatcherTimer? _holdProgressTimer;

    /// <summary>For tests: the progress tracker (its clock is the system tick count).</summary>
    internal HoldConfirmProgress HoldProgress => _holdProgress;

    /// <summary>Called on every key down / up before the engine sees it.</summary>
    internal void OnHoldConfirmKey(KeyItemViewModel key, bool isPressed)
    {
        if (isPressed)
        {
            int? ms = key.HoldToConfirmMs;
            // on a wallpaper deck the panel is composed as a whole; the bar is shown only on ordinary keys
            if (ms is not > 0 || IsPanoramicWallpaperActive) return;
            _holdProgress.Start(key.Index, ms.Value);
            DrawHoldProgress(key);
            EnsureHoldProgressTimer();
        }
        else
        {
            EndHoldProgress(key);
        }
    }

    /// <summary>Stops the bar of a key (released or its action has run) and draws the key without it.</summary>
    internal void EndHoldProgress(KeyItemViewModel key)
    {
        if (!_holdProgress.Stop(key.Index)) return;
        if (!_holdProgress.IsAnyRunning) _holdProgressTimer?.Stop();
        RenderKeyVisual(key, sendToHardware: true);
    }

    /// <summary>One frame: redraws the bar of every key that is being held.</summary>
    internal void TickHoldProgress()
    {
        foreach (int index in _holdProgress.Running)
        {
            if (index >= 0 && index < Keys.Count) DrawHoldProgress(Keys[index]);
        }
        if (!_holdProgress.IsAnyRunning) _holdProgressTimer?.Stop();
    }

    private void DrawHoldProgress(KeyItemViewModel key)
    {
        byte[]? basePicture = key.RenderedJpegBytes;
        if (basePicture == null || basePicture.Length == 0)
        {
            RenderKeyVisual(key, sendToHardware: false);
            basePicture = key.RenderedJpegBytes;
        }
        if (basePicture == null || basePicture.Length == 0) return;

        // the base picture stays the key's own; only the hardware (and nothing else) gets the picture with the bar
        byte[] withBar = ButtonImageRenderer.WithHoldProgress(basePicture, _holdProgress.Fraction(key.Index));
        try
        {
            _deviceService.SetKeyImage(key.Index, withBar);
        }
        catch
        {
            // a deck that went away mid-hold: the next connect draws every key again
        }
        LastHoldProgressPicture = withBar;
    }

    /// <summary>For tests: the last picture with a bar that was sent.</summary>
    internal byte[]? LastHoldProgressPicture { get; private set; }

    private void EnsureHoldProgressTimer()
    {
        if (_holdProgressTimer == null)
        {
            _holdProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoldConfirmProgress.FrameMs) };
            _holdProgressTimer.Tick += (_, _) => TickHoldProgress();
        }
        if (!_holdProgressTimer.IsEnabled) _holdProgressTimer.Start();
    }

    private void DisposeHoldProgress()
    {
        _holdProgressTimer?.Stop();
        _holdProgressTimer = null;
        _holdProgress.Clear();
    }
}

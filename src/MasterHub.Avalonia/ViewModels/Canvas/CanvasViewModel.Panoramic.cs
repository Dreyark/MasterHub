using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>
/// The wallpaper of the whole keypad: a still picture, a GIF or a video, zoomed and moved as one piece, with the text of the keys laid over it.
/// The wallpaper and the overlays of the keys are composed into one 480 x 272 picture that is sent to the keypad.
/// </summary>
public partial class CanvasViewModel
{
    private PanoramaDeck? _panorama;
    private int _panoramaVersion;
    private DispatcherTimer? _panoramaTransformTimer;

    /// <summary>How fast a moving wallpaper plays (frames per second).</summary>
    internal const int PanoramaFps = 10;

    internal PanoramaDeck? Panorama => _panorama;

    public void SyncPanoramicWallpaperStateToKeys()
    {
        bool isActive = IsPanoramicWallpaperActive;
        foreach (var k in Keys)
        {
            k.SetPanoramicState(isActive, PanoramicWallpaperPath, PanoramicWallpaperFitMode,
                PanoramicWallpaperScale, PanoramicWallpaperOffsetX, PanoramicWallpaperOffsetY);
        }
    }

    public void ApplyPanoramicWallpaper(string path, string fitMode = "Cover")
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;

        try
        {
            string stored = MasterHub.Avalonia.Services.MediaPaths.ToStore(path);   // copied into the media store (T-09)
            if (!string.Equals(PanoramicWallpaperPath, stored, StringComparison.OrdinalIgnoreCase))
            {
                // a new picture starts in the middle, at the size that fits
                PanoramicWallpaperScale = 100;
                PanoramicWallpaperOffsetX = 0;
                PanoramicWallpaperOffsetY = 0;
            }

            WallpaperMode = DeckWallpaperMode.PanoramicDeck;
            PanoramicWallpaperPath = stored;
            PanoramicWallpaperFitMode = fitMode;
            SavePanoramaToProfile();
            SyncPanoramicWallpaperStateToKeys();
            RestartPanorama();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Błąd nakładania tapety panoramicznej: {ex.Message}");
        }
    }

    /// <summary>Zoom and move of the whole wallpaper (from the sliders). It is redrawn a moment after the last change, so dragging a slider stays smooth.</summary>
    public void ApplyPanoramicTransform(int scale, int offsetX, int offsetY)
    {
        scale = Math.Clamp(scale, 20, 400);
        offsetX = Math.Clamp(offsetX, -100, 100);
        offsetY = Math.Clamp(offsetY, -100, 100);
        if (scale == PanoramicWallpaperScale && offsetX == PanoramicWallpaperOffsetX && offsetY == PanoramicWallpaperOffsetY) return;

        PanoramicWallpaperScale = scale;
        PanoramicWallpaperOffsetX = offsetX;
        PanoramicWallpaperOffsetY = offsetY;
        if (!IsPanoramicWallpaperActive) return;

        _panoramaTransformTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _panoramaTransformTimer.Tick -= OnPanoramaTransformSettled;
        _panoramaTransformTimer.Tick += OnPanoramaTransformSettled;
        _panoramaTransformTimer.Stop();
        _panoramaTransformTimer.Start();
    }

    internal void OnPanoramaTransformSettled(object? sender, EventArgs e)
    {
        _panoramaTransformTimer?.Stop();
        SavePanoramaToProfile();
        RestartPanorama();
    }

    private void SavePanoramaToProfile()
    {
        var profile = _profileService.GetActiveProfile();
        if (profile == null) return;

        profile.WallpaperMode = WallpaperMode;
        profile.PanoramicWallpaperPath = PanoramicWallpaperPath;
        profile.PanoramicWallpaperFitMode = PanoramicWallpaperFitMode;
        profile.PanoramicWallpaperScale = PanoramicWallpaperScale;
        profile.PanoramicWallpaperOffsetX = PanoramicWallpaperOffsetX;
        profile.PanoramicWallpaperOffsetY = PanoramicWallpaperOffsetY;

        EnsurePagesInitialized(profile);
        if (CurrentPageIndex >= 0 && CurrentPageIndex < profile.Pages.Count)
        {
            var page = profile.Pages[CurrentPageIndex];
            page.WallpaperMode = WallpaperMode;
            page.PanoramicWallpaperPath = PanoramicWallpaperPath;
            page.PanoramicWallpaperFitMode = PanoramicWallpaperFitMode;
            page.PanoramicWallpaperScale = PanoramicWallpaperScale;
            page.PanoramicWallpaperOffsetX = PanoramicWallpaperOffsetX;
            page.PanoramicWallpaperOffsetY = PanoramicWallpaperOffsetY;
        }

        _profileService.SaveProfile(profile);
    }

    public void ClearPanoramicWallpaper()
    {
        WallpaperMode = DeckWallpaperMode.PerKey;
        PanoramicWallpaperPath = null;
        PanoramicWallpaperScale = 100;
        PanoramicWallpaperOffsetX = 0;
        PanoramicWallpaperOffsetY = 0;
        SavePanoramaToProfile();
        SyncPanoramicWallpaperStateToKeys();
        StopPanorama();

        for (int i = 0; i < Keys.Count; i++)
        {
            RenderKeyVisual(Keys[i], sendToHardware: true);
        }
    }

    /// <summary>(Re)starts the wallpaper with the current file, fit, zoom and move. The overlays of the keys are drawn first, so the first picture already has them.</summary>
    internal void RestartPanorama()
    {
        if (!IsPanoramicWallpaperActive || string.IsNullOrWhiteSpace(PanoramicWallpaperPath) || !System.IO.File.Exists(PanoramicWallpaperPath))
        {
            StopPanorama();
            return;
        }

        var panorama = _panorama ??= CreatePanorama();
        string path = PanoramicWallpaperPath!;
        string fit = PanoramicWallpaperFitMode;
        int scale = PanoramicWallpaperScale, x = PanoramicWallpaperOffsetX, y = PanoramicWallpaperOffsetY;
        int version = ++_panoramaVersion;

        void Show(IReadOnlyList<byte[]> frames)
        {
            if (version != _panoramaVersion || _disposed) return;
            for (int i = 0; i < Keys.Count; i++) RenderKeyVisual(Keys[i], sendToHardware: false);   // the overlays
            panorama.SetSource(frames, PanoramaFps);
        }

        if (!PanoramaMedia.IsMoving(path))
        {
            Show(PanoramaMedia.ExtractFrames(path, fit, scale, x, y));   // a still picture is quick
            return;
        }

        // a GIF or a video takes a moment to decode: not on the UI thread
        Task.Run(() => PanoramaMedia.ExtractFrames(path, fit, scale, x, y)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) Dispatcher.UIThread.Post(() => Show(t.Result));
        }, TaskScheduler.Default);
    }

    private PanoramaDeck CreatePanorama() => new(
        jpeg => _deviceService.SetPanoramicWallpaper(jpeg),
        (key, jpeg) =>
        {
            if (_isUiSuspended) return;
            int version = _panoramaVersion;
            Dispatcher.UIThread.Post(() =>
            {
                if (version == _panoramaVersion && key < Keys.Count && IsPanoramicWallpaperActive) Keys[key].SetRenderedJpeg(jpeg);
            });
        });

    /// <summary>
    /// What a key puts on the wallpaper: nothing for an empty key (the wallpaper shows through), its own picture when it has one (that replaces its
    /// piece of the wallpaper), otherwise only its text and icon on a transparent picture.
    /// </summary>
    private byte[]? PanoramaOverlayFor(KeyItemViewModel key, byte[] opaqueJpeg, Func<byte[]> renderCustom)
    {
        if (key.AssignedAction == DeckActionType.StreamDeckPluginAction || key.HasCustomImage) return opaqueJpeg;

        bool hasText = !string.IsNullOrWhiteSpace(key.CustomTitle) || !string.IsNullOrWhiteSpace(key.CustomSubtitle) ||
                       key.AppliedLook.Title != null || key.AppliedLook.Subtitle != null || key.TimerTitleOverride != null;
        if (!key.HasAction && !hasText) return null;

        return ButtonImageRenderer.RenderOverlayPng(renderCustom);
    }

    private void OnStandbyForPanorama(bool standby)
    {
        _panorama?.SetPaused(standby);
        if (!standby) _panorama?.Refresh();   // the screen was blanked: the wallpaper goes out again
    }

    private void StopPanorama()
    {
        _panoramaVersion++;   // a decode that is still running is dropped
        _panorama?.Stop();
    }

    private void DisposePanorama()
    {
        _panoramaTransformTimer?.Stop();
        _panorama?.Dispose();
        _panorama = null;
    }
}

using System;
using System.IO;
using MasterHub.Core.Devices;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Implementacja silnika renderującego na klawisze LCD (Etap 3.1).
/// Integruje się z IMasterHubManager oraz ButtonImageRenderer.
/// </summary>
public sealed class KeyRenderer : IKeyRenderer
{
    private readonly IMasterHubManager? _manager;
    private readonly Action<int, byte[]>? _keyImageCallback;

    public event Action<int, byte[]>? KeyRendered;

    public KeyRenderer(IMasterHubManager? manager = null, Action<int, byte[]>? keyImageCallback = null)
    {
        _manager = manager;
        _keyImageCallback = keyImageCallback;
    }

    public void SetKeyImage(int keyIndex, string imagePath)
    {
        if (keyIndex < 0 || keyIndex > 14) return;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return;

        try
        {
            using var bmp = SKBitmap.Decode(imagePath);
            if (bmp != null)
            {
                SetKeyImageFromBitmap(keyIndex, bmp);
            }
        }
        catch { }
    }

    public void SetKeyImageFromBitmap(int keyIndex, SKBitmap bmp)
    {
        if (keyIndex < 0 || keyIndex > 14 || bmp == null) return;

        byte[] jpeg = ButtonImageRenderer.RenderBitmapJpeg(bmp);
        DispatchKeyJpeg(keyIndex, jpeg);
    }

    public void SetKeyText(int keyIndex, string text, SKColor bg, SKColor fg)
    {
        if (keyIndex < 0 || keyIndex > 14) return;

        byte[] jpeg = ButtonImageRenderer.RenderTextJpeg(text, bg, fg);
        DispatchKeyJpeg(keyIndex, jpeg);
    }

    public void ClearKey(int keyIndex)
    {
        if (keyIndex < 0 || keyIndex > 14) return;

        byte[] blank = ButtonImageRenderer.GetBlankJpeg();
        DispatchKeyJpeg(keyIndex, blank);
    }

    public void SetKeyImageFromJpeg(int keyIndex, byte[] jpeg)
    {
        if (keyIndex < 0 || keyIndex > 14 || jpeg == null) return;
        DispatchKeyJpeg(keyIndex, jpeg);
    }

    private void DispatchKeyJpeg(int keyIndex, byte[] jpeg)
    {
        _manager?.SetKeyImage(keyIndex, jpeg);
        _keyImageCallback?.Invoke(keyIndex, jpeg);
        KeyRendered?.Invoke(keyIndex, jpeg);
    }
}

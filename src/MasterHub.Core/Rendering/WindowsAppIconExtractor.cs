using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Pomocnik wyodrębniania ikon programów Windows (.exe / .lnk) w wysokiej rozdzielczości (128x128).
/// </summary>
public static class WindowsAppIconExtractor
{
    private static readonly ConcurrentDictionary<string, byte[]> s_iconCache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHDefExtractIconW(
        string pszIconFile,
        int iIndex,
        uint uFlags,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIconSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbmp,
        uint uStartScan,
        uint cScanLines,
        IntPtr lpvBits,
        ref BITMAPINFO lpbi,
        uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    public static SKBitmap? ExtractHighResAppIcon(string exePath, int size = 128)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return null;

        if (s_iconCache.TryGetValue(exePath, out var cachedBytes))
        {
            return SKBitmap.Decode(cachedBytes);
        }

        IntPtr hLarge = IntPtr.Zero;
        IntPtr hSmall = IntPtr.Zero;

        try
        {
            int hr = SHDefExtractIconW(exePath, 0, 0, out hLarge, out hSmall, (uint)size);
            if (hr != 0 || hLarge == IntPtr.Zero)
                return null;

            if (!GetIconInfo(hLarge, out var iconInfo))
                return null;

            try
            {
                if (iconInfo.hbmColor == IntPtr.Zero)
                    return null;

                GetObject(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), out var bm);
                int w = Math.Max(1, bm.bmWidth);
                int h = Math.Max(1, bm.bmHeight);

                IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero) return null;

                try
                {
                    var bmi = new BITMAPINFO
                    {
                        bmiHeader = new BITMAPINFOHEADER
                        {
                            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                            biWidth = w,
                            biHeight = -h,
                            biPlanes = 1,
                            biBitCount = 32,
                            biCompression = 0
                        }
                    };

                    int byteCount = w * h * 4;
                    byte[] pixels = new byte[byteCount];
                    GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    try
                    {
                        int lines = GetDIBits(hdc, iconInfo.hbmColor, 0, (uint)h, pin.AddrOfPinnedObject(), ref bmi, 0);
                        if (lines <= 0) return null;
                    }
                    finally
                    {
                        pin.Free();
                    }

                    using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul));
                    Marshal.Copy(pixels, 0, bmp.GetPixels(), pixels.Length);

                    using var image = SKImage.FromBitmap(bmp);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    byte[] pngBytes = data.ToArray();
                    s_iconCache[exePath] = pngBytes;

                    return SKBitmap.Decode(pngBytes);
                }
                finally
                {
                    DeleteDC(hdc);
                }
            }
            finally
            {
                if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
                if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hLarge != IntPtr.Zero) DestroyIcon(hLarge);
            if (hSmall != IntPtr.Zero) DestroyIcon(hSmall);
        }
    }
}

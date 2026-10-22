using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Serwis wyciągający ikony wysokiej rozdzielczości z plików .exe, .ico, skrótów Windows i gier Steam,
/// z zaawansowanym skalowaniem SkiaSharp do 72x72 z przezroczystością.
/// </summary>
public partial class AppIconExtractorService : IAppIconExtractorService
{
    private static readonly Regex SteamUrlRegex = new(@"steam:\/\/(?:rungameid|run)\/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumericOnlyRegex = new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex LibraryPathRegex = new(@"""path""\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex InstallDirRegex = new(@"""installdir""\s+""([^""]+)""", RegexOptions.Compiled);

    private readonly string _cacheDirectory;

    public AppIconExtractorService(string? customCacheDirectory = null)
    {
        _cacheDirectory = customCacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasterHub",
            "ExtractedIcons");

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
        catch
        {
            // Fallback do katalogu tymczasowego w razie braku uprawnień
            _cacheDirectory = Path.Combine(Path.GetTempPath(), "MasterHub_ExtractedIcons");
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public bool IsSteamUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        string trimmed = url.Trim();
        return SteamUrlRegex.IsMatch(trimmed) || NumericOnlyRegex.IsMatch(trimmed);
    }

    public string? ExtractSteamAppId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string trimmed = url.Trim();
        var match = SteamUrlRegex.Match(trimmed);
        if (match.Success) return match.Groups[1].Value;
        if (NumericOnlyRegex.IsMatch(trimmed)) return trimmed;
        return null;
    }

    public string? ResolveShortcutTarget(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
            return null;

        string ext = Path.GetExtension(shortcutPath).ToLowerInvariant();
        if (ext == ".url")
        {
            try
            {
                var lines = File.ReadAllLines(shortcutPath);
                string? iconFile = null;
                string? steamUrl = null;

                foreach (var line in lines)
                {
                    string l = line.Trim();
                    if (l.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                    {
                        iconFile = l.Substring(9).Trim('"');
                    }
                    else if (l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        string targetUrl = l.Substring(4).Trim();
                        if (IsSteamUrl(targetUrl))
                        {
                            steamUrl = targetUrl;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(iconFile) && File.Exists(iconFile))
                    return iconFile;

                if (!string.IsNullOrWhiteSpace(steamUrl))
                {
                    var appId = ExtractSteamAppId(steamUrl);
                    if (appId != null)
                    {
                        var resolved = ResolveSteamGameTarget(appId);
                        if (resolved != null) return resolved;
                    }
                }
            }
            catch { }
        }
        else if (ext == ".lnk")
        {
            try
            {
                var link = (IShellLinkW)new ShellLinkClass();
                ((IPersistFile)link).Load(shortcutPath, 0);

                var iconSb = new StringBuilder(260);
                link.GetIconLocation(iconSb, iconSb.Capacity, out _);
                string iconPath = iconSb.ToString();
                if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                    return iconPath;

                var targetSb = new StringBuilder(260);
                link.GetPath(targetSb, targetSb.Capacity, IntPtr.Zero, 0);
                string targetPath = targetSb.ToString();
                if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
                    return targetPath;
            }
            catch { }
        }

        return null;
    }

    public string? ResolveSteamGameTarget(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;

        // 1. Sprawdź czy na pulpicie lub w menu start jest skrót .url dla tej gry z gotową ikoną
        string? iconFromShortcut = FindSteamShortcutIcon(appId);
        if (!string.IsNullOrWhiteSpace(iconFromShortcut) && File.Exists(iconFromShortcut))
            return iconFromShortcut;

        // 2. Pobierz ścieżkę instalacji Steam z rejestru
        string? steamPath = GetSteamInstallPath();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
            return null;

        // 3. Sprawdź czy plik .ico istnieje bezpośrednio w <SteamPath>/steam/games/
        string steamGamesDir = Path.Combine(steamPath, "steam", "games");
        if (Directory.Exists(steamGamesDir))
        {
            string directIco = Path.Combine(steamGamesDir, $"{appId}.ico");
            if (File.Exists(directIco)) return directIco;

            var matchingIcons = Directory.GetFiles(steamGamesDir, $"*{appId}*.ico");
            if (matchingIcons.Length > 0) return matchingIcons[0];
        }

        // 4. Przeszukaj biblioteki Steam (libraryfolders.vdf) i appmanifest_<appid>.acf
        string? exePath = FindSteamGameExecutable(steamPath, appId);
        if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            return exePath;

        return null;
    }

    public SKBitmap? ExtractIconBitmap(string targetPathOrUrl, int targetSize = 72)
    {
        string? resolvedTarget = ResolveEffectiveTarget(targetPathOrUrl);
        if (string.IsNullOrWhiteSpace(resolvedTarget) || !File.Exists(resolvedTarget))
            return null;

        SKBitmap? sourceBitmap = null;
        string ext = Path.GetExtension(resolvedTarget).ToLowerInvariant();

        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".bmp")
        {
            try
            {
                sourceBitmap = SKBitmap.Decode(resolvedTarget);
            }
            catch { }
        }
        else if (ext == ".ico")
        {
            sourceBitmap = ExtractFromIcoFile(resolvedTarget) ?? ExtractViaWin32(resolvedTarget, 256);
        }
        else
        {
            sourceBitmap = ExtractViaWin32(resolvedTarget, 256);
        }

        if (sourceBitmap == null) return null;

        try
        {
            return ScaleToSquare(sourceBitmap, targetSize);
        }
        finally
        {
            sourceBitmap.Dispose();
        }
    }

    public byte[]? ExtractIconPngBytes(string targetPathOrUrl, int targetSize = 72)
    {
        using var bmp = ExtractIconBitmap(targetPathOrUrl, targetSize);
        if (bmp == null) return null;

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public string? ExtractAndSaveIcon(string targetPathOrUrl, int targetSize = 72)
    {
        if (string.IsNullOrWhiteSpace(targetPathOrUrl)) return null;

        string cacheKey = $"{targetPathOrUrl.Trim().ToLowerInvariant()}_{targetSize}";
        string hash = ComputeHash(cacheKey);
        string safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(targetPathOrUrl));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "app";
        string cacheFilePath = Path.Combine(_cacheDirectory, $"{safeName}_{hash}.png");

        // Jeśli plik już istnieje w pamięci podręcznej i jest aktualny
        if (File.Exists(cacheFilePath))
        {
            string? localFile = ResolveEffectiveTarget(targetPathOrUrl);
            if (localFile != null && File.Exists(localFile))
            {
                if (File.GetLastWriteTimeUtc(localFile) <= File.GetLastWriteTimeUtc(cacheFilePath))
                {
                    return cacheFilePath;
                }
            }
            else
            {
                return cacheFilePath;
            }
        }

        byte[]? pngBytes = ExtractIconPngBytes(targetPathOrUrl, targetSize);
        if (pngBytes == null || pngBytes.Length == 0) return null;

        try
        {
            File.WriteAllBytes(cacheFilePath, pngBytes);
            return cacheFilePath;
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveEffectiveTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        string trimmed = target.Trim().Trim('"', '\'');

        if (IsSteamUrl(trimmed))
        {
            var appId = ExtractSteamAppId(trimmed);
            if (appId != null)
            {
                return ResolveSteamGameTarget(appId);
            }
        }

        string ext = Path.GetExtension(trimmed).ToLowerInvariant();
        if (ext == ".lnk" || ext == ".url")
        {
            string? resolved = ResolveShortcutTarget(trimmed);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                return resolved;
        }

        if (File.Exists(trimmed))
            return trimmed;

        // Jeśli ścieżka zawiera argumenty, np. C:\Program Files\App\app.exe --arg
        if (trimmed.Contains(' ') && !File.Exists(trimmed))
        {
            string candidate = trimmed;
            while (candidate.Contains(' '))
            {
                int lastSpace = candidate.LastIndexOf(' ');
                candidate = candidate.Substring(0, lastSpace).Trim('"', '\'');
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static string? GetSteamInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key != null)
            {
                var val = key.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(val))
                    return val.Replace('/', '\\');

                var exeVal = key.GetValue("SteamExe") as string;
                if (!string.IsNullOrWhiteSpace(exeVal))
                    return Path.GetDirectoryName(exeVal.Replace('/', '\\'));
            }
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam") ??
                            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key != null)
            {
                var val = key.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(val))
                    return val.Replace('/', '\\');
            }
        }
        catch { }

        return null;
    }

    private static string? FindSteamShortcutIcon(string appId)
    {
        var searchDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        }.Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d));

        foreach (var dir in searchDirs)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.url", SearchOption.AllDirectories))
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        if (content.Contains($"rungameid/{appId}", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains($"run/{appId}", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(content, @"IconFile=([^\r\n]+)", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                string ico = match.Groups[1].Value.Trim().Trim('"');
                                if (File.Exists(ico)) return ico;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return null;
    }

    private static string? FindSteamGameExecutable(string steamPath, string appId)
    {
        var libraries = new System.Collections.Generic.List<string> { steamPath };
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (File.Exists(vdfPath))
        {
            try
            {
                string text = File.ReadAllText(vdfPath);
                var matches = LibraryPathRegex.Matches(text);
                foreach (Match m in matches)
                {
                    string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(lib) && !libraries.Any(x => string.Equals(x, lib, StringComparison.OrdinalIgnoreCase)))
                    {
                        libraries.Add(lib);
                    }
                }
            }
            catch { }
        }

        foreach (var lib in libraries)
        {
            string manifestPath = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (File.Exists(manifestPath))
            {
                try
                {
                    string manifest = File.ReadAllText(manifestPath);
                    var match = InstallDirRegex.Match(manifest);
                    if (match.Success)
                    {
                        string installDir = match.Groups[1].Value;
                        string commonDir = Path.Combine(lib, "steamapps", "common", installDir);
                        if (Directory.Exists(commonDir))
                        {
                            var exes = Directory.GetFiles(commonDir, "*.exe", SearchOption.AllDirectories)
                                .Where(e =>
                                {
                                    string fn = Path.GetFileName(e).ToLowerInvariant();
                                    return !fn.Contains("crash") && !fn.Contains("unins") &&
                                           !fn.Contains("setup") && !fn.Contains("redist") &&
                                           !fn.Contains("vcredist") && !fn.Contains("dxsetup") &&
                                           !fn.Contains("unitycrash");
                                })
                                .OrderByDescending(e =>
                                {
                                    string fn = Path.GetFileNameWithoutExtension(e);
                                    if (string.Equals(fn, installDir, StringComparison.OrdinalIgnoreCase)) return 100;
                                    if (installDir.Contains(fn, StringComparison.OrdinalIgnoreCase)) return 50;
                                    return 10;
                                })
                                .ToList();

                            if (exes.Count > 0) return exes[0];
                        }
                    }
                }
                catch { }
            }
        }

        return null;
    }

    private static SKBitmap? ExtractFromIcoFile(string icoPath)
    {
        try
        {
            using var stream = File.OpenRead(icoPath);
            using var reader = new BinaryReader(stream);
            ushort reserved = reader.ReadUInt16();
            ushort type = reader.ReadUInt16();
            if (reserved != 0 || type != 1) return null;

            ushort count = reader.ReadUInt16();
            if (count == 0) return null;

            int bestScore = -1;
            uint bestOffset = 0;
            uint bestBytes = 0;

            for (int i = 0; i < count; i++)
            {
                byte width = reader.ReadByte();
                byte height = reader.ReadByte();
                reader.ReadByte(); // colorCount
                reader.ReadByte(); // reserved
                reader.ReadUInt16(); // planes
                ushort bitCount = reader.ReadUInt16();
                uint bytesInRes = reader.ReadUInt32();
                uint imageOffset = reader.ReadUInt32();

                int actualWidth = width == 0 ? 256 : width;
                int actualHeight = height == 0 ? 256 : height;
                int score = actualWidth * actualHeight * (bitCount > 0 ? bitCount : 32);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = imageOffset;
                    bestBytes = bytesInRes;
                }
            }

            if (bestOffset > 0 && bestBytes > 0)
            {
                stream.Seek(bestOffset, SeekOrigin.Begin);
                byte[] buffer = reader.ReadBytes((int)bestBytes);

                if (buffer.Length >= 8 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
                {
                    using var ms = new MemoryStream(buffer);
                    return SKBitmap.Decode(ms);
                }
            }
        }
        catch { }

        return null;
    }

    private static SKBitmap? ExtractViaWin32(string path, int targetResolution = 256)
    {
        int[] sizes = targetResolution >= 256 ? [256, 128, 64, 48, 32] : [targetResolution, 256, 128, 64, 48, 32];

        foreach (int size in sizes)
        {
            IntPtr[] hicons = new IntPtr[1];
            uint[] ids = new uint[1];
            uint extracted = PrivateExtractIcons(path, 0, size, size, hicons, ids, 1, 0);

            if (extracted > 0 && hicons[0] != IntPtr.Zero)
            {
                try
                {
                    var bmp = ConvertHIconToSKBitmap(hicons[0]);
                    if (bmp != null) return bmp;
                }
                finally
                {
                    DestroyIcon(hicons[0]);
                }
            }
        }

        // Fallback: SHGetFileInfo
        var shinfo = new SHFILEINFO();
        IntPtr res = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
        {
            try
            {
                return ConvertHIconToSKBitmap(shinfo.hIcon);
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }

        return null;
    }

    private static SKBitmap? ConvertHIconToSKBitmap(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo)) return null;

        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            if (iconInfo.hbmColor != IntPtr.Zero)
            {
                GetObject(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), out BITMAP bmp);
                int width = bmp.bmWidth;
                int height = bmp.bmHeight;
                if (width <= 0 || height <= 0) return null;

                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = width;
                bmi.bmiHeader.biHeight = -height; // top-down DIB
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0; // BI_RGB

                byte[] pixels = new byte[width * height * 4];
                GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    GetDIBits(hdc, iconInfo.hbmColor, 0, (uint)height, pin.AddrOfPinnedObject(), ref bmi, 0);
                }
                finally
                {
                    pin.Free();
                }

                bool hasAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0)
                    {
                        hasAlpha = true;
                        break;
                    }
                }

                if (!hasAlpha && iconInfo.hbmMask != IntPtr.Zero)
                {
                    byte[] maskPixels = new byte[width * height * 4];
                    GCHandle maskPin = GCHandle.Alloc(maskPixels, GCHandleType.Pinned);
                    try
                    {
                        GetDIBits(hdc, iconInfo.hbmMask, 0, (uint)height, maskPin.AddrOfPinnedObject(), ref bmi, 0);
                    }
                    finally
                    {
                        maskPin.Free();
                    }

                    for (int i = 0; i < width * height; i++)
                    {
                        int pIdx = i * 4;
                        byte maskB = maskPixels[pIdx];
                        pixels[pIdx + 3] = maskB > 0 ? (byte)0 : (byte)255;
                    }
                }
                else if (!hasAlpha)
                {
                    for (int i = 3; i < pixels.Length; i += 4)
                    {
                        pixels[i] = 255;
                    }
                }

                var skBitmap = new SKBitmap();
                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                GCHandle pixelPin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    skBitmap.InstallPixels(info, pixelPin.AddrOfPinnedObject(), info.RowBytes, (addr, ctx) =>
                    {
                        ((GCHandle)ctx!).Free();
                    }, pixelPin);

                    return skBitmap.Copy();
                }
                catch
                {
                    pixelPin.Free();
                    skBitmap.Dispose();
                    return null;
                }
            }
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
            ReleaseDC(IntPtr.Zero, hdc);
        }

        return null;
    }

    private static SKBitmap ScaleToSquare(SKBitmap source, int targetSize)
    {
        var destBitmap = new SKBitmap(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(destBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            float scale = Math.Min((float)targetSize / source.Width, (float)targetSize / source.Height);
            float destWidth = source.Width * scale;
            float destHeight = source.Height * scale;
            float destX = (targetSize - destWidth) / 2f;
            float destY = (targetSize - destHeight) / 2f;

            var destRect = new SKRect(destX, destY, destX + destWidth, destY + destHeight);
            using var paint = new SKPaint
            {
                IsAntialias = true
            };
            canvas.DrawBitmap(source, destRect, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }
        return destBitmap;
    }

    private static string ComputeHash(string input)
    {
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).Substring(0, 12).ToLowerInvariant();
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "icon";
        return Regex.Replace(name, @"[^a-zA-Z0-9_\-]", "_");
    }

    #region Win32 P/Invoke & COM Definitions

    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[]? phicon,
        uint[]? piconid,
        uint nIcons,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbmp,
        uint uStartScan,
        uint cScanLines,
        IntPtr lpvBits,
        ref BITMAPINFO lpbi,
        uint uUsage);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

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
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public uint[] bmiColors;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkClass { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }

    #endregion
}

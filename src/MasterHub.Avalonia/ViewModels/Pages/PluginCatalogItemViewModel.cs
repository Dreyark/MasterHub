using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Integrations.StreamDeck;

namespace MasterHub.Avalonia.ViewModels.Pages;

public partial class PluginCatalogItemViewModel : ObservableObject
{
    private const int MaxCatalogIconCache = 64;
    private static readonly ConcurrentDictionary<string, Bitmap> s_iconCache = new();
    private static readonly ConcurrentQueue<string> s_iconKeys = new();
    private static readonly ConcurrentDictionary<string, byte> s_failedUrls = new();

    /// <remarks>
    /// An icon that leaves the cache is only <b>forgotten</b>, never disposed: the catalog list may still show it (the cache holds 64 icons
    /// and the catalog has hundreds), and an <c>Image</c> that arranges a disposed bitmap throws <see cref="ObjectDisposedException"/> in the
    /// layout pass. The bitmap is freed by the garbage collector once no item shows it any more.
    /// </remarks>
    internal static void AddToCache(string url, Bitmap bmp)
    {
        if (s_iconCache.TryAdd(url, bmp))
        {
            s_iconKeys.Enqueue(url);
            while (s_iconKeys.Count > MaxCatalogIconCache && s_iconKeys.TryDequeue(out var oldKey))
            {
                s_iconCache.TryRemove(oldKey, out _);
            }
        }
    }

    /// <summary>Forgets the cached icons (memory trimming). Not disposed, for the reason given at <see cref="AddToCache"/>.</summary>
    public static void ClearCache()
    {
        s_iconCache.Clear();
        while (s_iconKeys.TryDequeue(out _)) { }
    }
    private static readonly HttpClient s_imageClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(3),
        AutomaticDecompression = DecompressionMethods.All,
        MaxConnectionsPerServer = 4
    })
    {
        Timeout = TimeSpan.FromSeconds(4)
    };
    private static readonly System.Threading.SemaphoreSlim s_downloadThrottle = new(4, 4);

    public PluginCatalogEntry Entry { get; }

    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Author => Entry.Author;
    public string Description => Entry.Description;
    public string Category => string.IsNullOrWhiteSpace(Entry.Category) ? "Inne" : Entry.Category;
    public string StarsDisplay => Entry.Stars.HasValue && Entry.Stars.Value > 0 ? $"⭐ {Entry.Stars.Value}" : "";
    public bool HasDial => Entry.Dial;
    public string Repository => Entry.Repository;

    private Bitmap? _iconBitmap;
    private bool _iconRequested;

    public Bitmap? IconBitmap
    {
        get
        {
            if (!_iconRequested && !string.IsNullOrEmpty(Entry.Icon))
            {
                _iconRequested = true;
                _ = LoadIconAsync(Entry.Icon);
            }
            return _iconBitmap;
        }
        set => SetProperty(ref _iconBitmap, value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private bool _isInstalling;

    public string ButtonText => IsInstalling ? "Pobieranie..." : (IsInstalled ? "Zainstalowano" : "Zainstaluj");

    /// <summary>T-31 e: for an installed plugin, the badge of the compatibility check ("Runs here" / "Cannot run here" …).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompatBadge))]
    private string _compatBadge = "";

    public bool HasCompatBadge => !string.IsNullOrEmpty(CompatBadge);

    private readonly Func<PluginCatalogItemViewModel, Task>? _installAction;

    [RelayCommand]
    public async Task InstallAsync()
    {
        if (_installAction != null)
        {
            await _installAction(this);
        }
    }

    public PluginCatalogItemViewModel(PluginCatalogEntry entry, bool isInstalled, Func<PluginCatalogItemViewModel, Task>? installAction = null)
    {
        Entry = entry;
        _isInstalled = isInstalled;
        _installAction = installAction;
    }

    private async Task LoadIconAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (s_failedUrls.ContainsKey(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;

        if (s_iconCache.TryGetValue(url, out var cached))
        {
            IconBitmap = cached;
            return;
        }

        string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "cache", "icons");
        try { Directory.CreateDirectory(cacheDir); } catch { }
        string fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        string localPath = Path.Combine(cacheDir, fileHash + ".img");

        if (File.Exists(localPath))
        {
            try
            {
                byte[] diskBytes = File.ReadAllBytes(localPath);
                if (IsValidRasterImage(diskBytes))
                {
                    using var diskMs = new MemoryStream(diskBytes);
                    Bitmap diskBmp;
                    try { diskBmp = Bitmap.DecodeToWidth(diskMs, 72); }
                    catch { diskMs.Position = 0; diskBmp = new Bitmap(diskMs); }
                    AddToCache(url, diskBmp);
                    IconBitmap = diskBmp;
                    return;
                }
            }
            catch { }
        }

        bool acquired = false;
        try
        {
            await s_downloadThrottle.WaitAsync().ConfigureAwait(false);
            acquired = true;

            if (s_iconCache.TryGetValue(url, out cached))
            {
                IconBitmap = cached;
                return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation("User-Agent", "MasterHub/1.0");
            req.Headers.TryAddWithoutValidation("Accept", "image/png,image/webp,image/jpeg,image/*,*/*;q=0.8");

            using var resp = await s_imageClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                s_failedUrls.TryAdd(url, 0);
                return;
            }

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (!IsValidRasterImage(bytes))
            {
                s_failedUrls.TryAdd(url, 0);
                return;
            }

            try { File.WriteAllBytes(localPath, bytes); } catch { }

            using var ms = new MemoryStream(bytes);
            Bitmap bmp;
            try { bmp = Bitmap.DecodeToWidth(ms, 72); }
            catch { ms.Position = 0; bmp = new Bitmap(ms); }
            AddToCache(url, bmp);
            Dispatcher.UIThread.Post(() => IconBitmap = bmp);
        }
        catch (HttpRequestException)
        {
            s_failedUrls.TryAdd(url, 0);
        }
        catch (TaskCanceledException)
        {
            s_failedUrls.TryAdd(url, 0);
        }
        catch
        {
            s_failedUrls.TryAdd(url, 0);
        }
        finally
        {
            if (acquired) s_downloadThrottle.Release();
        }
    }

    private static bool IsValidRasterImage(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 8) return false;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true; // PNG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true; // JPEG
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return true; // GIF
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes.Length > 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true; // WebP
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true; // BMP
        if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00) return true; // ICO
        return false;
    }
}

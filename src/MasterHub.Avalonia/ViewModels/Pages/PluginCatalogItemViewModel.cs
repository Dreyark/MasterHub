using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Core.Integrations.StreamDeck;

namespace MasterHub.Avalonia.ViewModels.Pages;

public partial class PluginCatalogItemViewModel : ObservableObject
{
    private static readonly ConcurrentDictionary<string, Bitmap> s_iconCache = new();
    private static readonly HttpClient s_imageClient = new() { Timeout = TimeSpan.FromSeconds(5) };
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

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private bool _isInstalling;

    public string ButtonText => IsInstalling ? "Pobieranie..." : (IsInstalled ? "Zainstalowano" : "Zainstaluj");

    public PluginCatalogItemViewModel(PluginCatalogEntry entry, bool isInstalled)
    {
        Entry = entry;
        _isInstalled = isInstalled;

        if (!string.IsNullOrEmpty(entry.Icon))
        {
            _ = LoadIconAsync(entry.Icon);
        }
    }

    private async Task LoadIconAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;

        if (s_iconCache.TryGetValue(url, out var cached))
        {
            IconBitmap = cached;
            return;
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

            using var resp = await s_imageClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;

            string? contentType = resp.Content.Headers.ContentType?.MediaType;
            if (contentType != null && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return;

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (!IsValidRasterImage(bytes)) return;

            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            s_iconCache.TryAdd(url, bmp);
            Dispatcher.UIThread.Post(() => IconBitmap = bmp);
        }
        catch { }
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

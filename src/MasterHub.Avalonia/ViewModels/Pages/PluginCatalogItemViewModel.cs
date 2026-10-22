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
    private static readonly HttpClient s_imageClient = new() { Timeout = TimeSpan.FromSeconds(10) };

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
        if (s_iconCache.TryGetValue(url, out var cached))
        {
            IconBitmap = cached;
            return;
        }

        try
        {
            byte[] bytes = await s_imageClient.GetByteArrayAsync(url).ConfigureAwait(false);
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            s_iconCache.TryAdd(url, bmp);
            Dispatcher.UIThread.Post(() => IconBitmap = bmp);
        }
        catch { }
    }
}

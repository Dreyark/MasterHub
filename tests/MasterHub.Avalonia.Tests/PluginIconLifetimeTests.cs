using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Integrations.StreamDeck;
using SkiaSharp;

namespace MasterHub.UiTests;

/// <summary>
/// An <c>Image</c> that is handed a disposed bitmap throws <see cref="ObjectDisposedException"/> in the layout pass and takes the app down.
/// Browsing the Elgato catalog (hundreds of icons, a cache of 64) and refreshing the installed plugins both used to dispose bitmaps that were
/// still on screen.
/// </summary>
public sealed class PluginIconLifetimeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_plugin_icons_" + Guid.NewGuid().ToString("N"));

    public PluginIconLifetimeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        PluginCatalogItemViewModel.ClearCache();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static WriteableBitmap NewBitmap() => new(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    private string WritePng(string name)
    {
        string path = Path.Combine(_dir, name + ".png");
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [AvaloniaFact]
    public void AnIconThatLeavesTheCatalogCache_IsStillUsableByTheItemThatShowsIt()
    {
        var first = NewBitmap();
        PluginCatalogItemViewModel.AddToCache("https://example.test/first.png", first);

        for (int i = 0; i < 80; i++)   // the cache holds 64: the first one is long gone from it
            PluginCatalogItemViewModel.AddToCache($"https://example.test/{i}.png", NewBitmap());

        Assert.Equal(new Size(4, 4), first.Size);   // would throw if it had been disposed

        var image = new Image { Source = first };   // and an Image can still arrange it
        image.Measure(new Size(40, 40));
        image.Arrange(new Rect(0, 0, 40, 40));
    }

    [AvaloniaFact]
    public void TrimmingTheCache_DoesNotDisposeWhatIsOnScreen()
    {
        var bitmap = NewBitmap();
        PluginCatalogItemViewModel.AddToCache("https://example.test/shown.png", bitmap);

        PluginCatalogItemViewModel.ClearCache();   // what the tray does when the window is hidden

        Assert.Equal(new Size(4, 4), bitmap.Size);
    }

    /// <summary>The plugin host the store page talks to: it only needs to hand out the installed plugins.</summary>
    public class PluginHostProxy : DispatchProxy
    {
        public List<StreamDeckInstalledPlugin> Plugins { get; } = new();

        protected override object? Invoke(MethodInfo? m, object?[]? args)
        {
            if (m!.Name == "get_InstalledPlugins") return Plugins;
            var type = m.ReturnType;
            if (type == typeof(void)) return null;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type == typeof(bool)) return false;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    private static (IStreamDeckPluginHost Host, PluginHostProxy Proxy) NewHost()
    {
        var host = DispatchProxy.Create<IStreamDeckPluginHost, PluginHostProxy>();
        return (host, (PluginHostProxy)(object)host);
    }

    private StreamDeckInstalledPlugin Plugin(string name) => new() { PluginUuid = "com.test." + name, Name = name, IconPath = WritePng(name) };

    [AvaloniaFact]
    public void RefreshingTheInstalledPlugins_ReleasesTheOldIconsOnlyAfterTheyLeftTheScreen()
    {
        var (host, proxy) = NewHost();
        proxy.Plugins.Add(Plugin("one"));
        proxy.Plugins.Add(Plugin("two"));
        var page = new ActionStorePageViewModel(streamDeckHost: host);
        page.SelectedTabIndex = 1;   // the installed plugins tab

        var shown = page.StreamDeckPlugins.ToList();
        Assert.Equal(2, shown.Count);
        Assert.All(shown, p => Assert.NotNull(p.PluginBitmap));

        var window = new Window { Width = 900, Height = 700, Content = new ActionStorePageView { DataContext = page } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        proxy.Plugins.Clear();
        proxy.Plugins.Add(Plugin("three"));
        page.RefreshPlugins();

        // right after the refresh nothing is disposed yet: a layout pass in between is safe
        Assert.All(shown, p => Assert.Null(Record.Exception(() => _ = p.PluginBitmap!.Size)));
        window.UpdateLayout();

        // once the old items are off the screen their icons are released
        Dispatcher.UIThread.RunJobs();
        Assert.All(shown, p => Assert.Throws<ObjectDisposedException>(() => _ = p.PluginBitmap!.Size));

        Assert.Single(page.StreamDeckPlugins);
        window.UpdateLayout();   // the new list lays out without trouble
        window.Close();
    }
}

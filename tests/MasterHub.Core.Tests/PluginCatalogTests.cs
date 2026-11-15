using System.Net;
using System.Text.Json;
using MasterHub.Core.Integrations.StreamDeck;

namespace MasterHub.Core.Tests;

/// <summary>The plugin index comes from the public OpenAction registry (a JSON object keyed by plugin id); plugins themselves from GitHub.</summary>
public class PluginCatalogTests : IDisposable
{
    private const string Registry = """
    {
      "com.hmarr.amphetamine": {
        "name": "Amphetamine", "author": "Harry Marr",
        "repository": "https://github.com/hmarr/streamdeck-amphetamine",
        "description": "Stream Deck plugin for Amphetamine"
      },
      "com.5e.hwinfo-reader": {
        "name": "HWiNFO Reader", "author": "5e",
        "repository": "https://github.com/5e/streamdeck-hwinfo-plugin",
        "download_url": "https://github.com/5e/streamdeck-hwinfo-plugin/raw/refs/heads/main/com.5e.hwinfo-reader.streamDeckPlugin",
        "description": null
      },
      "me.dwot.keylight": {
        "name": "Keylight", "author": "dwot", "repository": "https://github.com/dwot/keylight",
        "description": "Elgato Key Light", "flags": { "ai_assisted": true }
      },
      "../evil id": { "name": "Evil", "repository": "https://github.com/x/y" },
      "no.repo": { "name": "Nothing to download" },
      "no.name": { "repository": "https://github.com/a/b" },
      "not.an.object": 5
    }
    """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_catalog_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private PluginCatalogService Service(FakeHandler handler) =>
        new(new HttpClient(handler), cachePath: Path.Combine(_dir, "cache.json"));

    [Fact]
    public void TheRegistryShape_IsParsedIntoEntries_AndUnusableEntriesAreDropped()
    {
        var list = PluginCatalogParser.Parse(Registry);

        Assert.Equal(new[] { "com.hmarr.amphetamine", "com.5e.hwinfo-reader", "me.dwot.keylight", "../evil id" }, list.Select(e => e.Id));
        var amph = list[0];
        Assert.Equal("Amphetamine", amph.Name);
        Assert.Equal("Harry Marr", amph.Author);
        Assert.Equal("https://github.com/hmarr/streamdeck-amphetamine", amph.Repository);
        Assert.Null(amph.DownloadUrl);
        Assert.Equal("", list[1].Description);   // a null description becomes an empty text
        Assert.EndsWith(".streamDeckPlugin", list[1].DownloadUrl);
        Assert.True(list[2].AiAssisted);
        Assert.False(amph.AiAssisted);
    }

    [Fact]
    public void Icons_PointAtTheRegistry_ButNeverForAnUnsafeId()
    {
        var list = PluginCatalogParser.Parse(Registry);

        Assert.Equal("https://openactionapi.github.io/plugins/icons/com.hmarr.amphetamine.png", list[0].Icon);
        Assert.Equal("", list.Single(e => e.Id == "../evil id").Icon);
    }

    [Theory]
    [InlineData("com.example.plugin", true)]
    [InlineData("a-b_c.1", true)]
    [InlineData("../x", false)]
    [InlineData("a/b", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyPlainIdsAreSafe(string? id, bool safe) => Assert.Equal(safe, PluginCatalogParser.IsSafeId(id));

    [Fact]
    public void TheEarlierShape_StillParses_SoAnOldCacheKeepsWorking()
    {
        string old = JsonSerializer.Serialize(new PluginCatalogFile
        {
            Plugins = { new PluginCatalogEntry { Id = "old.one", Name = "Old", Repository = "https://github.com/o/o" } }
        });

        var list = PluginCatalogParser.Parse(old);

        Assert.Equal("old.one", Assert.Single(list).Id);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"text\"")]
    public void AnEmptyOrWrongDocument_GivesNoEntries(string json) => Assert.Empty(PluginCatalogParser.Parse(json));

    [Fact]
    public async Task TheCatalogIsFetchedFromTheRegistry_AndCachedOnDisk()
    {
        var handler = new FakeHandler(_ => Ok(Registry));
        using var service = Service(handler);

        var list = await service.GetCatalogAsync();

        Assert.Equal(PluginCatalogService.DefaultCatalogUrl, handler.Requests.Single());
        Assert.Equal("https://openactionapi.github.io/plugins/catalogue.json", handler.Requests[0]);
        Assert.Equal(4, list.Count);
        Assert.True(File.Exists(Path.Combine(_dir, "cache.json")));
    }

    [Fact]
    public async Task WhenTheNetworkFails_TheCacheOnDiskIsUsed()
    {
        using (var online = Service(new FakeHandler(_ => Ok(Registry))))
            await online.GetCatalogAsync();

        using var offline = Service(new FakeHandler(_ => throw new HttpRequestException("no network")));
        var list = await offline.GetCatalogAsync(forceRefresh: true);

        Assert.Equal(4, list.Count);
        Assert.Contains(list, e => e.Id == "com.hmarr.amphetamine");
    }

    [Fact]
    public async Task WithNoNetworkAndNoCache_TheListIsEmpty_NotAnError()
    {
        using var service = Service(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        Assert.Empty(await service.GetCatalogAsync());
    }

    [Fact]
    public async Task AGarbageAnswer_FallsBackInsteadOfThrowing()
    {
        using var service = Service(new FakeHandler(_ => Ok("<html>not json</html>")));

        Assert.Empty(await service.GetCatalogAsync());
    }

    [Fact]
    public async Task APluginWithADirectLink_IsDownloadedFromThatLink_WithoutTheGitHubApi()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var handler = new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var service = Service(handler);
        var entry = PluginCatalogParser.Parse(Registry).Single(e => e.Id == "com.5e.hwinfo-reader");

        string? file = await service.DownloadPluginPackageAsync(entry);

        try
        {
            Assert.NotNull(file);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(file!));
            Assert.Equal(entry.DownloadUrl, handler.Requests.Single());
        }
        finally { if (file != null) File.Delete(file); }
    }

    [Fact]
    public async Task APluginWithOnlyARepository_IsFoundInTheLatestGitHubRelease()
    {
        var handler = new FakeHandler(r =>
        {
            string url = r.RequestUri!.ToString();
            if (url.EndsWith("/releases/latest"))
                return Ok("""{"assets":[{"name":"readme.txt","browser_download_url":"https://x/readme.txt"},{"name":"com.hmarr.amphetamine.streamDeckPlugin","browser_download_url":"https://github.com/hmarr/streamdeck-amphetamine/releases/download/v1/com.hmarr.amphetamine.streamDeckPlugin"}]}""");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9 }) };
        });
        using var service = Service(handler);
        var entry = PluginCatalogParser.Parse(Registry).Single(e => e.Id == "com.hmarr.amphetamine");

        string? file = await service.DownloadPluginPackageAsync(entry);

        try
        {
            Assert.NotNull(file);
            Assert.Equal("https://api.github.com/repos/hmarr/streamdeck-amphetamine/releases/latest", handler.Requests[0]);
            Assert.EndsWith(".streamDeckPlugin", handler.Requests[1]);
        }
        finally { if (file != null) File.Delete(file); }
    }

    [Fact]
    public async Task AnInsecureDownloadLink_IsRefused()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) });
        using var service = Service(handler);
        var entry = new PluginCatalogEntry { Id = "x", Name = "X", DownloadUrl = "http://example.com/x.streamDeckPlugin" };

        Assert.Null(await service.DownloadPluginPackageAsync(entry));
        Assert.Empty(handler.Requests);
    }
}

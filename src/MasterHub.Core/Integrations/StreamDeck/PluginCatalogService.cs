using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.StreamDeck;

public class PluginCatalogService : IPluginCatalogService, IDisposable
{
    private const string CatalogUrl = "https://przemotar.github.io/hubdeck/plugins.json";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _cachePath;
    private List<PluginCatalogEntry>? _memoryCache;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PluginCatalogService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectTimeout = TimeSpan.FromSeconds(5)
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MasterHub/1.0 (Windows NT 10.0; Win64; x64)");
            _ownsHttpClient = true;
        }

        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub");
        Directory.CreateDirectory(appData);
        _cachePath = Path.Combine(appData, "catalog_cache.json");
    }

    public async Task<IReadOnlyList<PluginCatalogEntry>> GetCatalogAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _memoryCache != null && _memoryCache.Count > 0)
            {
                return _memoryCache;
            }

            // 1. Spróbuj pobrać z internetu
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, CatalogUrl);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    var catalogFile = await JsonSerializer.DeserializeAsync<PluginCatalogFile>(stream, cancellationToken: ct).ConfigureAwait(false);
                    if (catalogFile != null && catalogFile.Plugins.Count > 0)
                    {
                        // Filtruj do wspierających Windows (lub ogólnych)
                        var list = catalogFile.Plugins
                            .Where(p => p.Systems.Count == 0 || p.Systems.Any(s => s.Contains("Windows", StringComparison.OrdinalIgnoreCase)))
                            .ToList();

                        _memoryCache = list;

                        // Zapisz do pamięci podręcznej na dysku
                        try
                        {
                            string json = JsonSerializer.Serialize(catalogFile);
                            await File.WriteAllTextAsync(_cachePath, json, ct).ConfigureAwait(false);
                        }
                        catch { }

                        return list;
                    }
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Błąd sieci - przejdź do cache dyskowego
            }

            // 2. Fallback do dysku
            if (File.Exists(_cachePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_cachePath, ct).ConfigureAwait(false);
                    var catalogFile = JsonSerializer.Deserialize<PluginCatalogFile>(json);
                    if (catalogFile != null && catalogFile.Plugins.Count > 0)
                    {
                        var list = catalogFile.Plugins
                            .Where(p => p.Systems.Count == 0 || p.Systems.Any(s => s.Contains("Windows", StringComparison.OrdinalIgnoreCase)))
                            .ToList();
                        _memoryCache = list;
                        return list;
                    }
                }
                catch { }
            }

            if (_memoryCache != null) return _memoryCache;
            return Array.Empty<PluginCatalogEntry>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> DownloadPluginPackageAsync(PluginCatalogEntry entry, CancellationToken ct = default)
    {
        string? directUrl = entry.DownloadUrl;

        // Jeśli brak bezpośredniego linku, spróbuj odnaleźć w GitHub Releases
        if (string.IsNullOrWhiteSpace(directUrl) && !string.IsNullOrWhiteSpace(entry.Repository))
        {
            directUrl = await ResolveGitHubReleaseDownloadUrlAsync(entry.Repository, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(directUrl))
        {
            return null;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, directUrl);
            req.Headers.UserAgent.ParseAdd("MasterHub/1.0");
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            string ext = directUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".streamDeckPlugin";
            string tempFile = Path.Combine(Path.GetTempPath(), $"{entry.Id}_{Guid.NewGuid():N}{ext}");

            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            return tempFile;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ResolveGitHubReleaseDownloadUrlAsync(string repoUrl, CancellationToken ct)
    {
        try
        {
            string marker = "github.com/";
            int idx = repoUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            string slug = repoUrl[(idx + marker.Length)..].TrimEnd('/', '.');
            if (slug.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) slug = slug[..^4];
            var parts = slug.Split('/');
            if (parts.Length < 2) return null;

            string owner = parts[0];
            string repo = parts[1];

            // 1. Sprawdź latest release
            string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            string? assetUrl = await QueryGitHubReleaseAssetAsync(apiUrl, ct).ConfigureAwait(false);
            if (assetUrl != null) return assetUrl;

            // 2. Jeśli brak latest (np. prereleases), sprawdź ostatnie wydania
            string listUrl = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=5";
            using var reqList = new HttpRequestMessage(HttpMethod.Get, listUrl);
            reqList.Headers.UserAgent.ParseAdd("MasterHub/1.0");
            using var respList = await _httpClient.SendAsync(reqList, ct).ConfigureAwait(false);
            if (respList.IsSuccessStatusCode)
            {
                await using var stream = await respList.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rel in doc.RootElement.EnumerateArray())
                    {
                        var found = PickAssetFromReleaseElement(rel);
                        if (found != null) return found;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private async Task<string?> QueryGitHubReleaseAssetAsync(string apiUrl, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.UserAgent.ParseAdd("MasterHub/1.0");
            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return PickAssetFromReleaseElement(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string? PickAssetFromReleaseElement(JsonElement releaseElement)
    {
        if (!releaseElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var candidates = new List<(string Name, string Url)>();
        foreach (var a in assets.EnumerateArray())
        {
            string? name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
            {
                string lower = name.ToLowerInvariant();
                if (lower.EndsWith(".streamdeckplugin") || lower.EndsWith(".zip"))
                {
                    candidates.Add((lower, url));
                }
            }
        }

        if (candidates.Count == 0) return null;

        // Priorytetyzuj assety dla Windows
        var winAsset = candidates.FirstOrDefault(c => c.Name.Contains("win") || c.Name.Contains("windows"));
        if (winAsset.Url != null) return winAsset.Url;

        // Jeśli brak oznaczenia systemowego, weź pierwszy .streamdeckplugin
        var sdAsset = candidates.FirstOrDefault(c => c.Name.EndsWith(".streamdeckplugin"));
        if (sdAsset.Url != null) return sdAsset.Url;

        return candidates[0].Url;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        _lock.Dispose();
    }
}

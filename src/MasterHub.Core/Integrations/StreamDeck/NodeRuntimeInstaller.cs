using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>The result of a Node.js download.</summary>
public sealed record NodeInstallResult(bool Success, string? NodePath, string? Version, string? Error);

/// <summary>
/// Downloads Node.js for plugins that need it (T-31 b) – <b>only after the user agreed</b> (the caller asks first). It takes
/// the newest LTS build of the required major version from the official index (<c>nodejs.org/dist/index.json</c>), downloads
/// the Windows x64 zip and the release's <c>SHASUMS256.txt</c>, <b>checks the SHA-256</b>, and extracts only
/// <c>node.exe</c> into <c>%APPDATA%\MasterHub\runtime\node-v&lt;version&gt;</c>. Nothing is installed system-wide and no
/// PATH is changed; deleting the folder removes it.
/// </summary>
public sealed class NodeRuntimeInstaller
{
    public const string DistBase = "https://nodejs.org/dist/";

    /// <summary>Downloads larger than this are refused (the zip is ≈ 30 MB).</summary>
    public const long MaxZipBytes = 120L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly string _runtimeRoot;

    public NodeRuntimeInstaller(HttpClient? http = null, string? runtimeRoot = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _runtimeRoot = runtimeRoot ?? NodeResolver.ManagedRuntimeRoot;
    }

    /// <summary>The newest version of <paramref name="major"/> in the official index (LTS preferred), e.g. <c>v20.11.1</c>.</summary>
    internal static string? PickVersion(string indexJson, int major)
    {
        using var doc = JsonDocument.Parse(indexJson);
        string prefix = "v" + major + ".";
        string? any = null;
        foreach (var release in doc.RootElement.EnumerateArray())   // the index is newest first
        {
            string? version = release.TryGetProperty("version", out var v) ? v.GetString() : null;
            if (version == null || !version.StartsWith(prefix, StringComparison.Ordinal)) continue;
            bool hasWinZip = release.TryGetProperty("files", out var files) && files.EnumerateArray().Any(f => f.GetString() == "win-x64-zip");
            if (!hasWinZip) continue;
            bool lts = release.TryGetProperty("lts", out var l) && l.ValueKind == JsonValueKind.String;
            if (lts) return version;
            any ??= version;
        }
        return any;
    }

    /// <summary>The SHA-256 of <paramref name="fileName"/> in a <c>SHASUMS256.txt</c>.</summary>
    internal static string? ExpectedHash(string shasums, string fileName)
    {
        foreach (string line in shasums.Split('\n'))
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1] == fileName && parts[0].Length == 64) return parts[0].ToLowerInvariant();
        }
        return null;
    }

    public async Task<NodeInstallResult> InstallAsync(int major, CancellationToken cancellationToken = default)
    {
        try
        {
            string index = await _http.GetStringAsync(DistBase + "index.json", cancellationToken).ConfigureAwait(false);
            string? version = PickVersion(index, major);
            if (version == null) return new NodeInstallResult(false, null, null, $"nodejs.org lists no Windows build of Node {major}.");

            string zipName = $"node-{version}-win-x64.zip";
            string shasums = await _http.GetStringAsync($"{DistBase}{version}/SHASUMS256.txt", cancellationToken).ConfigureAwait(false);
            string? expected = ExpectedHash(shasums, zipName);
            if (expected == null) return new NodeInstallResult(false, null, version, "The checksum of the download is not published.");

            byte[] zip;
            using (var response = await _http.GetAsync($"{DistBase}{version}/{zipName}", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxZipBytes) return new NodeInstallResult(false, null, version, "The download is unexpectedly large.");
                zip = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            if (zip.LongLength > MaxZipBytes) return new NodeInstallResult(false, null, version, "The download is unexpectedly large.");

            string actual = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
            if (actual != expected) return new NodeInstallResult(false, null, version, "The download does not match its published checksum – nothing was installed.");

            string target = Path.Combine(_runtimeRoot, "node-" + version);
            string nodePath = Path.Combine(target, "node.exe");
            Directory.CreateDirectory(target);
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                // only node.exe from the top folder of the archive (node-vX-win-x64/node.exe); nothing else is extracted
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals($"node-{version}-win-x64/node.exe", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return new NodeInstallResult(false, null, version, "node.exe is not in the download.");
                string tmp = nodePath + ".tmp";
                entry.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, nodePath, overwrite: true);
            }
            return new NodeInstallResult(true, nodePath, version, null);
        }
        catch (OperationCanceledException)
        {
            return new NodeInstallResult(false, null, null, "Cancelled.");
        }
        catch (Exception ex)
        {
            return new NodeInstallResult(false, null, null, ex.Message);
        }
    }
}

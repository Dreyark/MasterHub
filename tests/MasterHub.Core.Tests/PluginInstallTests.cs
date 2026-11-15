using System.IO.Compression;
using MasterHub.Core.Integrations.StreamDeck;

namespace MasterHub.Core.Tests;

public sealed class PluginInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh_plug_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task InstallPackage_ZipSlipEntriesAreNotExtractedOutsidePluginFolder()
    {
        string plugins = Path.Combine(_root, "plugins");
        string elgato = Path.Combine(_root, "elgato");
        Directory.CreateDirectory(plugins);
        Directory.CreateDirectory(elgato);

        string pkg = Path.Combine(_root, "evil.streamDeckPlugin");
        using (var zip = ZipFile.Open(pkg, ZipArchiveMode.Create))
        {
            AddEntry(zip, "com.test.evil.sdPlugin/manifest.json",
                "{\"UUID\":\"com.test.evil\",\"Name\":\"Evil\",\"Version\":\"1.0\",\"CodePath\":\"plugin.js\",\"Actions\":[]}");
            AddEntry(zip, "com.test.evil.sdPlugin/../../escaped.txt", "pwned");
            AddEntry(zip, "com.test.evil.sdPlugin/..\\..\\escaped2.txt", "pwned");
        }

        using var host = new StreamDeckPluginHost(28999, plugins, elgato);
        await host.InstallPluginPackageAsync(pkg);

        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "escaped2.txt")));
        Assert.False(File.Exists(Path.Combine(plugins, "escaped.txt")));
        Assert.True(File.Exists(Path.Combine(plugins, "com.test.evil.sdPlugin", "manifest.json")));
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}

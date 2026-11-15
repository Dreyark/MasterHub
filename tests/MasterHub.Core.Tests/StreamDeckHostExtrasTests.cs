using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Actions;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Profiles;
using Xunit;

namespace MasterHub.Core.Tests;

/// <summary>T-31: Stream Deck host extras – states &gt; 2, Node resolver + download, compatibility, CPU watchdog, usage.</summary>
public class StreamDeckHostExtrasTests
{
    // ── d: multi-state actions ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 4, false, 1)]
    [InlineData(3, 4, false, 0)]      // wraps around
    [InlineData(1, 4, true, 1)]       // DisableAutomaticStates: stays
    [InlineData(0, 1, false, 0)]      // a single-state action never moves
    [InlineData(7, 3, false, 0)]      // an out-of-range state is clamped first (2), then moves on (0)
    public void Next_MovesThroughEveryState(int state, int count, bool disable, int expected)
    {
        Assert.Equal(expected, PluginKeyStates.Next(state, count, disable));
    }

    [Fact]
    public void AfterKeyUp_ReportsTheStateWhilePressed_AndKeepsTheNextOne()
    {
        var states = new PluginKeyStates();
        states.Set(2, 2, 3);

        var (reported, current) = states.AfterKeyUp(2, 3, false, out bool changed);

        Assert.Equal(2, reported);
        Assert.Equal(0, current);
        Assert.True(changed);
        Assert.Equal(0, states.Get(2));
        Assert.Equal(5, states.Set(1, 9, 6));   // setState is clamped to the states the action has
    }

    [Fact]
    public void StateImages_AreFoundWithTheSdkExtensions_AndStayInsideThePluginFolder()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(@"C:\p", "imgs", "s2@2x.png") };

        Assert.Equal(Path.Combine(@"C:\p", "imgs", "s2@2x.png"), StreamDeckPluginHost.ResolveStateImage(@"C:\p", "imgs/s2", files.Contains));
        Assert.Null(StreamDeckPluginHost.ResolveStateImage(@"C:\p", "../../Windows/x", _ => true));
        Assert.Null(StreamDeckPluginHost.ResolveStateImage(@"C:\p", @"C:\Windows\x.png", _ => true));
        Assert.Null(StreamDeckPluginHost.ResolveStateImage(@"C:\p", "", _ => true));
    }

    // ── b: Node.js resolver ─────────────────────────────────────────────────────────────────

    private sealed class FakeFs : INodeFileSystem
    {
        public Dictionary<string, Version?> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Env { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public IEnumerable<string> Directories(string path) => Dirs.TryGetValue(path, out var d) ? d : new List<string>();
        public string? Environment(string name) => Env.TryGetValue(name, out var v) ? v : null;
        public Version? FileVersion(string path) => Files.TryGetValue(path, out var v) ? v : null;
    }

    [Theory]
    [InlineData("20", 20)]
    [InlineData("v18", 18)]
    [InlineData("20.11.1", 20)]
    [InlineData(null, NodeResolver.DefaultRequiredMajor)]
    [InlineData("latest", NodeResolver.DefaultRequiredMajor)]
    public void TheRequiredMajor_IsReadFromTheManifest(string? version, int expected)
    {
        Assert.Equal(expected, NodeResolver.RequiredMajor(version));
    }

    [Fact]
    public void TheNewestDownloadedRuntime_IsPreferred()
    {
        var fs = new FakeFs();
        fs.Dirs[@"R:\runtime"] = new() { @"R:\runtime\node-v18.20.0", @"R:\runtime\node-v20.11.1" };
        fs.Files[@"R:\runtime\node-v18.20.0\node.exe"] = null;
        fs.Files[@"R:\runtime\node-v20.11.1\node.exe"] = null;   // no file version: read from the folder name
        fs.Env["PATH"] = @"C:\other";
        fs.Files[@"C:\other\node.exe"] = new Version(22, 1, 0);

        var r = NodeResolver.Resolve(fs, 20, @"R:\runtime");

        Assert.Equal(NodeProblem.None, r.Problem);
        Assert.Equal(@"R:\runtime\node-v20.11.1\node.exe", r.Path);
        Assert.Equal(new Version(20, 11, 1), r.Version);
    }

    [Fact]
    public void ANodeThatIsTooOld_IsReported_InsteadOfStartingACrash()
    {
        var fs = new FakeFs();
        fs.Env["ProgramFiles"] = @"C:\PF";
        fs.Files[@"C:\PF\nodejs\node.exe"] = new Version(16, 20, 2);

        var r = NodeResolver.Resolve(fs, 20, @"R:\none");

        Assert.Equal(NodeProblem.TooOld, r.Problem);
        Assert.Equal(new Version(16, 20, 2), r.Version);
    }

    [Fact]
    public void NoNode_IsMissing_AndANewerOneOnPathIsFound()
    {
        var fs = new FakeFs();
        Assert.Equal(NodeProblem.Missing, NodeResolver.Resolve(fs, 20, @"R:\none").Problem);

        fs.Env["PATH"] = @"C:\a; C:\tools\node ";
        fs.Files[@"C:\tools\node\node.exe"] = new Version(20, 0, 0);
        Assert.Equal(@"C:\tools\node\node.exe", NodeResolver.Resolve(fs, 20, @"R:\none").Path);
    }

    // ── b: consented download ───────────────────────────────────────────────────────────────

    private sealed class FakeDist : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files;
        public List<string> Requested { get; } = new();
        public FakeDist(Dictionary<string, byte[]> files) => _files = files;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requested.Add(url);
            return Task.FromResult(_files.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static byte[] Zip(string version, byte[] nodeExe)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var s = zip.CreateEntry($"node-{version}-win-x64/node.exe").Open()) s.Write(nodeExe);
            using (var s = zip.CreateEntry($"node-{version}-win-x64/npm.cmd").Open()) s.Write(Encoding.ASCII.GetBytes("not extracted"));
        }
        return ms.ToArray();
    }

    private const string Index = "[" +
        "{\"version\":\"v22.1.0\",\"lts\":false,\"files\":[\"win-x64-zip\"]}," +
        "{\"version\":\"v20.12.0\",\"lts\":false,\"files\":[\"win-x64-zip\"]}," +
        "{\"version\":\"v20.11.1\",\"lts\":\"Iron\",\"files\":[\"win-x64-zip\"]}," +
        "{\"version\":\"v20.11.0\",\"lts\":\"Iron\",\"files\":[\"linux-x64\"]}]";

    [Fact]
    public void ThePickedVersion_IsTheNewestLtsOfTheMajor_WithAWindowsBuild()
    {
        Assert.Equal("v20.11.1", NodeRuntimeInstaller.PickVersion(Index, 20));
        Assert.Equal("v22.1.0", NodeRuntimeInstaller.PickVersion(Index, 22));   // no LTS: the newest
        Assert.Null(NodeRuntimeInstaller.PickVersion(Index, 14));
    }

    [Fact]
    public async Task TheDownload_IsCheckedAgainstItsChecksum_AndOnlyNodeExeIsExtracted()
    {
        string root = Path.Combine(Path.GetTempPath(), "mh_node_" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] exe = Encoding.ASCII.GetBytes("MZ fake node");
            byte[] zip = Zip("v20.11.1", exe);
            string sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
            var dist = new FakeDist(new()
            {
                [NodeRuntimeInstaller.DistBase + "index.json"] = Encoding.UTF8.GetBytes(Index),
                [NodeRuntimeInstaller.DistBase + "v20.11.1/SHASUMS256.txt"] = Encoding.UTF8.GetBytes($"{new string('0', 64)}  other.zip\n{sha}  node-v20.11.1-win-x64.zip\n"),
                [NodeRuntimeInstaller.DistBase + "v20.11.1/node-v20.11.1-win-x64.zip"] = zip
            });

            var result = await new NodeRuntimeInstaller(new HttpClient(dist), root).InstallAsync(20);

            Assert.True(result.Success, result.Error);
            Assert.Equal(Path.Combine(root, "node-v20.11.1", "node.exe"), result.NodePath);
            Assert.Equal(exe, File.ReadAllBytes(result.NodePath!));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "node-v20.11.1")));   // npm.cmd was not extracted
            Assert.All(dist.Requested, u => Assert.StartsWith("https://nodejs.org/dist/", u));

            // and the resolver now finds it
            var found = NodeResolver.Resolve(RealNodeFileSystem.Instance, 20, root);
            Assert.Equal(result.NodePath, found.Path);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task AWrongChecksum_InstallsNothing()
    {
        string root = Path.Combine(Path.GetTempPath(), "mh_node_" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] zip = Zip("v20.11.1", new byte[] { 1, 2, 3 });
            var dist = new FakeDist(new()
            {
                [NodeRuntimeInstaller.DistBase + "index.json"] = Encoding.UTF8.GetBytes(Index),
                [NodeRuntimeInstaller.DistBase + "v20.11.1/SHASUMS256.txt"] = Encoding.UTF8.GetBytes($"{new string('a', 64)}  node-v20.11.1-win-x64.zip\n"),
                [NodeRuntimeInstaller.DistBase + "v20.11.1/node-v20.11.1-win-x64.zip"] = zip
            });

            var result = await new NodeRuntimeInstaller(new HttpClient(dist), root).InstallAsync(20);

            Assert.False(result.Success);
            Assert.Contains("checksum", result.Error);
            Assert.False(Directory.Exists(Path.Combine(root, "node-v20.11.1")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    // ── f / e: compatibility ────────────────────────────────────────────────────────────────

    private sealed class FakeEnv : IPluginRuntimeEnvironment
    {
        public NodeResolution Node { get; set; } = new(@"C:\node.exe", new Version(20, 0), NodeProblem.None, 20);
        public bool HasBrowser { get; set; } = true;
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public NodeResolution ResolveNode(int requiredMajor) => Node with { RequiredMajor = requiredMajor };
        public bool FileExists(string path) => Files.Contains(path);
    }

    private static StreamDeckInstalledPlugin Plugin(Action<StreamDeckInstalledPlugin>? setup = null)
    {
        var p = new StreamDeckInstalledPlugin { PluginUuid = "com.x", Name = "X", Version = "1.0", ExecutablePath = @"C:\p\x.exe", Manifest = new StreamDeckManifest() };
        setup?.Invoke(p);
        return p;
    }

    [Fact]
    public void ANativePlugin_WithItsProgram_Runs()
    {
        var env = new FakeEnv();
        env.Files.Add(@"C:\p\x.exe");

        var r = PluginCompatibility.Check(Plugin(), env);
        Assert.Equal(PluginVerdict.Runs, r.Verdict);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void EveryReason_IsNamed()
    {
        var env = new FakeEnv();
        Assert.Contains(PluginCompatibility.Check(Plugin(), env).Issues, i => i.Issue == PluginIssue.ExecutableMissing);

        var mac = Plugin(p => p.Manifest.OS = new() { new StreamDeckOs { Platform = "mac" } });
        env.Files.Add(@"C:\p\x.exe");
        Assert.Equal(PluginVerdict.CannotRun, PluginCompatibility.Check(mac, env).Verdict);

        var node = Plugin(p => { p.IsNodeJs = true; p.Manifest.Nodejs = new StreamDeckNodejs { Version = "20" }; });
        env.Node = new NodeResolution(null, null, NodeProblem.Missing, 20);
        Assert.Equal("20", Assert.Single(PluginCompatibility.Check(node, env).Issues).Detail);
        env.Node = new NodeResolution(@"C:\n.exe", new Version(16, 1, 0), NodeProblem.TooOld, 20);
        Assert.Equal("20|16.1.0", Assert.Single(PluginCompatibility.Check(node, env).Issues).Detail);

        var html = Plugin(p => p.IsHtml = true);
        env.HasBrowser = false;
        Assert.Equal(PluginIssue.NoBrowser, Assert.Single(PluginCompatibility.Check(html, env).Issues).Issue);
    }

    [Theory]
    [InlineData("linux", "Linux")]
    [InlineData("mac", "macOS")]
    [InlineData("macOS", "macOS")]
    public void APluginForAnotherSystem_NamesThatSystem(string platform, string shown)
    {
        var env = new FakeEnv();
        env.Files.Add(@"C:\p\x.exe");
        var p = Plugin(pl => pl.Manifest.OS = new() { new StreamDeckOs { Platform = platform } });

        var r = PluginCompatibility.Check(p, env);

        Assert.False(r.CanRun);
        var issue = Assert.Single(r.Issues);
        Assert.Equal(PluginIssue.OtherSystemOnly, issue.Issue);
        Assert.Equal(shown, issue.Detail);
    }

    [Fact]
    public void ALinuxAndWindowsPlugin_IsFine()
    {
        var env = new FakeEnv();
        env.Files.Add(@"C:\p\x.exe");
        var p = Plugin(pl => pl.Manifest.OS = new() { new StreamDeckOs { Platform = "linux" }, new StreamDeckOs { Platform = "windows" } });

        Assert.Equal(PluginVerdict.Runs, PluginCompatibility.Check(p, env).Verdict);
    }

    [Fact]
    public void AShellScriptLauncher_WithNoSystemListed_CannotRunOnWindows()
    {
        var env = new FakeEnv();
        env.Files.Add(@"C:\p\run.sh");
        var p = Plugin(pl => pl.ExecutablePath = @"C:\p\run.sh");

        var r = PluginCompatibility.Check(p, env);

        Assert.False(r.CanRun);
        Assert.Equal("Linux", Assert.Single(r.Issues).Detail);
    }

    [Fact]
    public void ANewerStreamDeck_OnlyMakesItPartial()
    {
        var env = new FakeEnv();
        env.Files.Add(@"C:\p\x.exe");
        var p = Plugin(pl => pl.Manifest.Software = new StreamDeckSoftware { MinimumVersion = "6.9" });

        var r = PluginCompatibility.Check(p, env);
        Assert.Equal(PluginVerdict.Partial, r.Verdict);
        Assert.True(r.CanRun);
    }

    [Fact]
    public void TheCompatibilityTable_HasOneRowPerPlugin()
    {
        var env = new FakeEnv();
        string md = PluginCompatibility.ToMarkdown(new[]
        {
            (Plugin(p => p.Name = "B|ar"), PluginCompatibility.Check(Plugin(), env)),
            (Plugin(p => p.Name = "Alpha"), new PluginCompatReport(PluginVerdict.Runs, Array.Empty<PluginIssueItem>()))
        });

        var lines = md.Trim().Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("| Alpha", lines[2]);
        Assert.Contains("B\\|ar", lines[3]);
        Assert.Contains("ExecutableMissing", lines[3]);
    }

    // ── a: CPU watchdog ─────────────────────────────────────────────────────────────────────

    private sealed class FakeSampler : IProcessCpuSampler
    {
        public Dictionary<int, TimeSpan> Cpu { get; } = new();
        public TimeSpan? TotalProcessorTime(int processId) => Cpu.TryGetValue(processId, out var t) ? t : null;
    }

    [Fact]
    public void ABusyPlugin_IsFlagged_AndTheFlagClearsWithHysteresis()
    {
        var now = new DateTime(2026, 1, 1);
        var sampler = new FakeSampler();
        var dog = new PluginCpuWatchdog(sampler, () => now);
        var running = new Dictionary<string, int> { ["busy"] = 10 };

        void Step(double percentOfOneCore)
        {
            now += TimeSpan.FromSeconds(5);
            sampler.Cpu[10] += TimeSpan.FromMilliseconds(5000 * percentOfOneCore / 100);
        }

        sampler.Cpu[10] = TimeSpan.Zero;
        Assert.Empty(dog.Sample(running));          // baseline
        Step(60); Assert.Empty(dog.Sample(running)); // one sample is not enough
        Step(60);
        var flagged = Assert.Single(dog.Sample(running));
        Assert.True(flagged.IsHeavy);
        Assert.Equal(60, flagged.PercentOfOneCore, 0);

        for (int i = 0; i < 2; i++) { Step(20); Assert.Empty(dog.Sample(running)); }   // between 15 and 25: stays flagged
        for (int i = 0; i < 6; i++) { Step(1); dog.Sample(running); }
        Assert.False(dog.Current("busy").IsHeavy);
    }

    [Fact]
    public void AStoppedPlugin_LosesItsFlag()
    {
        var now = new DateTime(2026, 1, 1);
        var sampler = new FakeSampler { Cpu = { [5] = TimeSpan.Zero } };
        var dog = new PluginCpuWatchdog(sampler, () => now);
        var running = new Dictionary<string, int> { ["p"] = 5 };
        dog.Sample(running);
        for (int i = 0; i < 3; i++) { now += TimeSpan.FromSeconds(5); sampler.Cpu[5] += TimeSpan.FromSeconds(5); dog.Sample(running); }
        Assert.True(dog.Current("p").IsHeavy);

        var change = Assert.Single(dog.Sample(new Dictionary<string, int>()));
        Assert.False(change.IsHeavy);
    }

    // ── c: keys that use a plugin ───────────────────────────────────────────────────────────

    [Fact]
    public void TheUsageScan_FindsEveryKeyOfThePlugin_InEveryProfile()
    {
        var key = new KeyAssignmentModel { Index = 2, Action = DeckActionType.StreamDeckPluginAction, StreamDeckPluginUuid = "com.x" };
        var other = new KeyAssignmentModel { Index = 3, Action = DeckActionType.StreamDeckPluginAction, StreamDeckPluginUuid = "com.y" };
        var a = new ProfileModel { Name = "A", Keys = new() { key, other } };
        var b = new ProfileModel { Name = "B", Pages = new() { new KeyPageModel(), new KeyPageModel { Keys = new() { new KeyAssignmentModel { Index = 0, Action = DeckActionType.StreamDeckPluginAction, StreamDeckPluginUuid = "COM.X" } } } } };

        var found = PluginUsageScanner.Find(new[] { a, b }, "com.x");

        Assert.Equal(new[] { new PluginUsage("A", "1", 3), new PluginUsage("B", "2", 1) }, found);
        Assert.Empty(PluginUsageScanner.Find(new[] { a }, ""));
    }
}

using System.Text.Json;
using MasterHub.Core.Apps;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Tests;

/// <summary>T-36: installed apps/games catalog – parsers on fixtures, sources on a temp directory, cover-art privacy.</summary>
public sealed class InstalledAppsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_installed_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const string LibraryFoldersNew = """
        "libraryfolders"
        {
            "0"
            {
                "path"      "C:\\Program Files (x86)\\Steam"
                "label"     ""
                "apps" { "730" "1234" }
            }
            "1"
            {
                "path"      "D:\\SteamLibrary"
            }
            "contentstatsid" "123456"
        }
        """;

    private const string LibraryFoldersOld = """
        "LibraryFolders"
        {
            "TimeNextStatsReport" "123"
            "0" "E:\\Games\\Steam"
            "1" "F:\\More"
        }
        """;

    private const string CounterStrikeAcf = """
        "AppState"
        {
            "appid"        "730"
            "Universe"     "1"
            "name"         "Counter-Strike 2"
            "StateFlags"   "4"
            "installdir"   "Counter-Strike Global Offensive"
        }
        """;

    // ── Steam ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Vdf_ParsesNestedBlocksCommentsAndEscapes()
    {
        var root = VdfParser.Parse("""
            // comment
            "a" { "b" "C:\\dir\\x" "c" { "d" "1" } }
            """);

        var a = Assert.IsType<Dictionary<string, object>>(root["a"]);
        Assert.Equal(@"C:\dir\x", a["b"]);
        Assert.Equal("1", Assert.IsType<Dictionary<string, object>>(a["c"])["d"]);
    }

    [Fact]
    public void Steam_LibraryFolders_BothLayouts()
    {
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" },
            InstalledAppParsers.ParseSteamLibraryFolders(LibraryFoldersNew));
        Assert.Equal(new[] { @"E:\Games\Steam", @"F:\More" },
            InstalledAppParsers.ParseSteamLibraryFolders(LibraryFoldersOld));
    }

    [Fact]
    public void Steam_Manifest_BecomesAGameWithALaunchLink()
    {
        var app = InstalledAppParsers.ParseSteamManifest(CounterStrikeAcf, coverPath: @"C:\cache\730.jpg");

        Assert.NotNull(app);
        Assert.Equal("steam:730", app!.Id);
        Assert.Equal("Counter-Strike 2", app.Name);
        Assert.Equal("steam://rungameid/730", app.LaunchTarget);
        Assert.True(app.IsGame);
    }

    [Theory]
    [InlineData("\"AppState\" { \"appid\" \"228980\" \"name\" \"Steamworks Common Redistributables\" \"StateFlags\" \"4\" }")]
    [InlineData("\"AppState\" { \"appid\" \"1\" \"name\" \"Half-installed\" \"StateFlags\" \"1026\" }")]
    [InlineData("\"AppState\" { \"appid\" \"abc\" \"name\" \"Bad id\" }")]
    [InlineData("not a manifest")]
    public void Steam_Manifest_SkipsToolsUnfinishedAndGarbage(string acf)
    {
        Assert.Null(InstalledAppParsers.ParseSteamManifest(acf));
    }

    [Fact]
    public void SteamSource_ScansAllLibraryFolders_AndFindsCoversInTheLauncherCache()
    {
        string steam = Path.Combine(_dir, "Steam");
        string second = Path.Combine(_dir, "Library2");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(second, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steam, "appcache", "librarycache"));

        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"" + steam.Replace("\\", "\\\\") + "\" } \"1\" { \"path\" \"" + second.Replace("\\", "\\\\") + "\" } }");
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_730.acf"), CounterStrikeAcf);
        File.WriteAllText(Path.Combine(second, "steamapps", "appmanifest_440.acf"),
            "\"AppState\" { \"appid\" \"440\" \"name\" \"Team Fortress 2\" \"StateFlags\" \"4\" }");
        string cover = Path.Combine(steam, "appcache", "librarycache", "730_library_600x900.jpg");
        File.WriteAllBytes(cover, new byte[] { 1, 2, 3 });

        var apps = new SteamSource(steam).Scan(CancellationToken.None).OrderBy(a => a.Id).ToList();

        Assert.Equal(new[] { "steam:440", "steam:730" }, apps.Select(a => a.Id));
        Assert.Equal(cover, apps.Single(a => a.Id == "steam:730").CoverPath);
        Assert.Null(apps.Single(a => a.Id == "steam:440").CoverPath);
    }

    // ── Epic ────────────────────────────────────────────────────────────────────────────────

    private const string EpicItem = """
        {
          "FormatVersion": 0,
          "bIsIncompleteInstall": false,
          "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteClient-Win64-Shipping.exe",
          "InstallLocation": "C:/Program Files/Epic Games/Fortnite",
          "DisplayName": "Fortnite",
          "CatalogNamespace": "fn",
          "CatalogItemId": "4fe75bbc5a674f4f9b356b5c90567da5",
          "AppName": "Fortnite"
        }
        """;

    [Fact]
    public void Epic_Manifest_BecomesAGameWithALauncherLink()
    {
        var app = InstalledAppParsers.ParseEpicManifest(EpicItem);

        Assert.NotNull(app);
        Assert.Equal("epic:Fortnite", app!.Id);
        Assert.Equal("Fortnite", app.Name);
        Assert.StartsWith("com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch", app.LaunchTarget);
        Assert.Equal("FortniteClient-Win64-Shipping", app.ProcessName);
    }

    [Theory]
    [InlineData("""{"DisplayName":"DLC","AppName":"dlc","LaunchExecutable":""}""")]
    [InlineData("""{"DisplayName":"Half","AppName":"h","LaunchExecutable":"a.exe","bIsIncompleteInstall":true}""")]
    [InlineData("{ broken")]
    public void Epic_Manifest_SkipsDlcIncompleteAndGarbage(string json)
    {
        Assert.Null(InstalledAppParsers.ParseEpicManifest(json));
    }

    [Fact]
    public void EpicSource_ReadsItemFiles()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "A.item"), EpicItem);
        File.WriteAllText(Path.Combine(_dir, "B.item"), "garbage");

        var apps = new EpicSource(_dir).Scan(CancellationToken.None).ToList();

        Assert.Single(apps);
    }

    // ── GOG / Store / Start menu ───────────────────────────────────────────────────────────

    [Fact]
    public void Gog_RegistryValues_BecomeAGame()
    {
        var values = new Dictionary<string, string>
        {
            ["gameID"] = "1207658924", ["gameName"] = "The Witcher", ["exe"] = @"C:\GOG\Witcher\witcher.exe"
        };

        var app = InstalledAppParsers.ParseGogGame(k => values.GetValueOrDefault(k));

        Assert.Equal("gog:1207658924", app!.Id);
        Assert.Equal("witcher", app.ProcessName);
        Assert.Null(InstalledAppParsers.ParseGogGame(_ => null));
    }

    [Fact]
    public void StartApps_KeepsOnlyStoreApps()
    {
        const string json = """
            [
              {"Name":"Calculator","AppID":"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"},
              {"Name":"Notepad++","AppID":"C:\\Program Files\\Notepad++\\notepad++.exe"},
              {"Name":"Paint","AppID":"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\\mspaint.exe"},
              {"Name":"","AppID":"X!Y"}
            ]
            """;

        var apps = InstalledAppParsers.ParseStartAppsJson(json);

        var calc = Assert.Single(apps);
        Assert.Equal("Calculator", calc.Name);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", calc.LaunchTarget);
    }

    [Fact]
    public void StartApps_SingleObjectAndGarbageAreHandled()
    {
        Assert.Single(InstalledAppParsers.ParseStartAppsJson("""{"Name":"One","AppID":"Pkg_1!App"}"""));
        Assert.Empty(InstalledAppParsers.ParseStartAppsJson("not json"));
        Assert.Empty(InstalledAppParsers.ParseStartAppsJson(""));
    }

    [Fact]
    public void StartMenuSource_ResolvesShortcuts_AndSkipsUninstallers()
    {
        string programs = Path.Combine(_dir, "Programs");
        Directory.CreateDirectory(Path.Combine(programs, "Tools"));
        foreach (string name in new[] { "Notepad.lnk", "Uninstall Notepad.lnk", "Help.lnk" })
            File.WriteAllText(Path.Combine(programs, "Tools", name), "x");

        string? Resolve(string lnk) => Path.GetFileName(lnk) switch
        {
            "Notepad.lnk" => @"C:\Tools\notepad.exe",
            "Help.lnk" => @"C:\Tools\help.chm", // not an exe: skipped
            _ => @"C:\Tools\unins000.exe"
        };

        var apps = new StartMenuSource(new[] { programs }, Resolve).Scan(CancellationToken.None).ToList();

        var app = Assert.Single(apps);
        Assert.Equal("Notepad", app.Name);
        Assert.Equal("notepad", app.ProcessName);
        Assert.EndsWith("Notepad.lnk", app.LaunchTarget);
    }

    // ── service & privacy ───────────────────────────────────────────────────────────────────

    private sealed class FakeSource : IInstalledAppSource
    {
        private readonly Func<IEnumerable<InstalledApp>> _scan;
        public FakeSource(Func<IEnumerable<InstalledApp>> scan) => _scan = scan;
        public string Name => "fake";
        public IEnumerable<InstalledApp> Scan(CancellationToken cancellationToken) => _scan();
    }

    [Fact]
    public async Task Service_MergesSortsDedupesAndCaches_AndOneBrokenSourceDoesNotHideTheOthers()
    {
        string cache = Path.Combine(_dir, "installed.json");
        var service = new InstalledAppsService(cache, new IInstalledAppSource[]
        {
            new FakeSource(() => new[]
            {
                new InstalledApp("steam:2", "Zelda", InstalledAppKind.Steam, "steam://rungameid/2") { CoverPath = @"C:\secret\cover.jpg" },
                new InstalledApp("steam:1", "Alpha", InstalledAppKind.Steam, "steam://rungameid/1")
            }),
            new FakeSource(() => throw new InvalidOperationException("launcher is broken")),
            new FakeSource(() => new[] { new InstalledApp("steam:1", "Alpha duplicate", InstalledAppKind.Steam, "x") })
        });

        int changed = 0;
        service.Changed += () => changed++;
        await service.RefreshAsync();

        Assert.Equal(new[] { "Alpha", "Zelda" }, service.Apps.Select(a => a.Name));
        Assert.Equal(1, changed);
        Assert.False(service.IsScanning);
        Assert.NotNull(service.LastScanUtc);

        // the cache is loaded by the next instance, without a scan
        var reloaded = new InstalledAppsService(cache, Array.Empty<IInstalledAppSource>());
        Assert.Equal(new[] { "Alpha", "Zelda" }, reloaded.Apps.Select(a => a.Name));
    }

    [Fact]
    public async Task CoverPaths_NeverReachTheCacheOrAProfile()
    {
        string cache = Path.Combine(_dir, "installed.json");
        var app = new InstalledApp("steam:730", "CS2", InstalledAppKind.Steam, "steam://rungameid/730") { CoverPath = @"C:\Steam\appcache\730.jpg" };
        var service = new InstalledAppsService(cache, new IInstalledAppSource[] { new FakeSource(() => new[] { app }) });
        await service.RefreshAsync();

        Assert.DoesNotContain("CoverPath", File.ReadAllText(cache));
        Assert.DoesNotContain("appcache", File.ReadAllText(cache));
        Assert.DoesNotContain("CoverPath", JsonSerializer.Serialize(app));

        // and nothing in the profile model can hold an InstalledApp (exports/backups serialise ProfileModel only)
        Assert.DoesNotContain(typeof(ProfileModel).GetProperties(), p => p.PropertyType.FullName?.Contains("InstalledApp") == true);
        Assert.DoesNotContain("CoverPath", JsonSerializer.Serialize(ProfileModel.CreateEmpty("x")));
    }

    [Fact]
    public async Task ConcurrentRefresh_IsIgnoredWhileScanning()
    {
        var gate = new ManualResetEventSlim();
        int scans = 0;
        var service = new InstalledAppsService(Path.Combine(_dir, "c.json"), new IInstalledAppSource[]
        {
            new FakeSource(() => { Interlocked.Increment(ref scans); gate.Wait(5000); return Array.Empty<InstalledApp>(); })
        });

        var first = service.RefreshAsync();
        SpinWait.SpinUntil(() => service.IsScanning, 2000);
        await service.RefreshAsync(); // returns immediately: a scan is already running
        gate.Set();
        await first;

        Assert.Equal(1, scans);
    }
}

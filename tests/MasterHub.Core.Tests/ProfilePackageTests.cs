using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterHub.Core.Actions;
using MasterHub.Core.LiveValues;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Packages;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

/// <summary>T-09: media store, .mhdeck export/import round trip, hostile packages and the "programs" scan.</summary>
public sealed class ProfilePackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh_pkg_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private sealed class Plain : ISecretProtectionService
    {
        public bool IsProtected(string? value) => false;
        public string? Protect(string? plainText) => plainText;
        public string? Unprotect(string? cipherText) => cipherText;
    }

    private static byte[] Png(byte seed = 1) => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, seed, seed, 1, 2, 3 };

    private (ProfileService Profiles, MediaStore Media, ProfilePackageService Packages) Machine(string name)
    {
        string dir = Path.Combine(_root, name);
        var profiles = new ProfileService(dir, new Plain());
        var media = new MediaStore(dir);
        return (profiles, media, new ProfilePackageService(profiles, media));
    }

    private string Picture(string name, byte seed = 1)
    {
        Directory.CreateDirectory(Path.Combine(_root, "pics"));
        string path = Path.Combine(_root, "pics", name);
        File.WriteAllBytes(path, Png(seed));
        return path;
    }

    // ── media store ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Store_CopiesByContent_AndSurvivesTheOriginalBeingDeleted()
    {
        var (_, media, _) = Machine("a");
        string original = Picture("logo.PNG");

        string stored = media.Import(original)!;

        Assert.True(media.IsStorePath(stored));
        Assert.EndsWith(".png", stored);                                    // extension normalised
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Png())).ToLowerInvariant() + ".png", Path.GetFileName(stored));

        File.Delete(original);
        Assert.True(File.Exists(stored));                                   // the profile keeps working

        Assert.Equal(stored, media.Import(Picture("copy.png")));            // same content = same file
        Assert.Equal(stored, media.Import(stored));                         // already in the store: unchanged
        Assert.Single(Directory.GetFiles(media.Directory));
    }

    [Fact]
    public void Store_MissingFile_ReturnsNull_AndJpegIsStoredAsJpg()
    {
        var (_, media, _) = Machine("a");
        Assert.Null(media.Import(Path.Combine(_root, "nope.png")));
        Assert.Null(media.Import(""));

        string jpeg = Path.Combine(_root, "photo.JPEG");
        File.WriteAllBytes(jpeg, new byte[] { 0xFF, 0xD8, 1, 2, 3 });
        Assert.EndsWith(".jpg", media.Import(jpeg));
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.JPG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.svg", true)]
    [InlineData("a.mp4", false)]
    [InlineData("a.exe", false)]
    [InlineData("", false)]
    public void PackableFormats(string path, bool expected) => Assert.Equal(expected, MediaStore.IsPackable(path));

    // ── round trip ──────────────────────────────────────────────────────────────────────────

    private ProfileModel BuildProfile(ProfileService profiles, MediaStore media, out string picturePath)
    {
        picturePath = media.Import(Picture("logo.png", 7))!;
        var profile = profiles.CreateProfile("Streaming", setAsActive: false);
        profile.ObsPassword = "secret-password";
        profile.Keys = new List<KeyAssignmentModel>
        {
            new() { Index = 0, Action = DeckActionType.MediaPlayPause, CustomTitle = "Play", ImagePath = picturePath, BackgroundColorHex = "#112233" },
            new() { Index = 1, Action = DeckActionType.MediaNext, ActiveImagePath = picturePath, ImagePath = Path.Combine(_root, "gone.png") }
        };
        profile.Pages = new List<KeyPageModel> { new() { PageIndex = 0, Name = "Page", Keys = profile.Keys } };
        profile.PanoramicWallpaperPath = picturePath;
        profile.SmartRules.Add(new SmartProfileRule { ProfileId = profile.Id, ProcessName = "obs64" });
        profiles.SaveProfile(profile);
        return profile;
    }

    [Fact]
    public void RoundTrip_OnAnotherMachine_KeepsKeysAndPictures_ButNeverCredentialsOrIds()
    {
        var a = Machine("a");
        var original = BuildProfile(a.Profiles, a.Media, out _);
        string package = Path.Combine(_root, "share", "Streaming.mhdeck");

        a.Packages.Export(original.Id, package);

        // the package holds the profile and the picture only, and no secret
        using (var zip = ZipFile.OpenRead(package))
        {
            Assert.Contains(zip.Entries, e => e.FullName == "profile.json");
            Assert.Single(zip.Entries, e => e.FullName.StartsWith("images/"));
            string json = new StreamReader(zip.GetEntry("profile.json")!.Open()).ReadToEnd();
            Assert.DoesNotContain("secret-password", json);
            Assert.DoesNotContain(_root.Replace("\\", "\\\\"), json);   // no local path leaks into the package
        }

        // another PC (another data folder)
        var b = Machine("b");
        var preview = b.Packages.Inspect(package);
        Assert.Equal(1, preview.MissingPictures == 0 ? 1 : 0);          // gone.png was dropped on export, not "missing" on import
        var imported = b.Packages.Commit(preview);

        Assert.NotEqual(original.Id, imported.Id);                      // always a new identity
        Assert.False(imported.IsDefault);
        Assert.Empty(imported.SmartRules);                              // linked apps belong to the author's PC

        var saved = b.Profiles.GetAllProfiles().Single(p => p.Id == imported.Id);
        var key0 = saved.Keys.Single(k => k.Index == 0);
        Assert.Equal("Play", key0.CustomTitle);
        Assert.Equal("#112233", key0.BackgroundColorHex);
        Assert.True(b.Media.IsStorePath(key0.ImagePath));               // the picture lives in this machine's store
        Assert.Equal(Png(7), File.ReadAllBytes(key0.ImagePath));
        Assert.True(b.Media.IsStorePath(saved.PanoramicWallpaperPath));
        Assert.Equal("", saved.Keys.Single(k => k.Index == 1).ImagePath);  // the missing picture was dropped: key keeps icon + label
        Assert.True(b.Media.IsStorePath(saved.Keys.Single(k => k.Index == 1).ActiveImagePath));
    }

    [Fact]
    public void Import_NeverReplacesAnExistingProfile()
    {
        var a = Machine("a");
        var original = BuildProfile(a.Profiles, a.Media, out _);
        string package = Path.Combine(_root, "p.mhdeck");
        a.Packages.Export(original.Id, package);

        var imported = a.Packages.Commit(a.Packages.Inspect(package));    // into the same machine

        var all = a.Profiles.GetAllProfiles();
        Assert.Contains(all, p => p.Id == original.Id);
        Assert.Contains(all, p => p.Id == imported.Id);
        Assert.NotEqual(original.Name, imported.Name);                    // "(Import)" suffix
        Assert.Contains(a.Profiles.Backups.List(), b => b.Reason == "before-import");
    }

    // ── hostile packages ───────────────────────────────────────────────────────────────────

    private string MakeZip(string name, Action<ZipArchive> fill)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(_root);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) fill(zip);
        return path;
    }

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(bytes);
    }

    private static byte[] ProfileJson(params string[] imagePaths)
    {
        var keys = imagePaths.Select((p, i) => $"{{\"Index\":{i},\"ImagePath\":{JsonSerializer.Serialize(p)}}}");
        return Encoding.UTF8.GetBytes($"{{\"Name\":\"X\",\"Keys\":[{string.Join(",", keys)}]}}");
    }

    [Fact]
    public void TraversalNames_AreIgnored_NeverWritten()
    {
        string zip = MakeZip("evil.mhdeck", z =>
        {
            Add(z, "profile.json", ProfileJson("images/ok.png", "images/../../evil.png", "C:\\Windows\\win.ini"));
            Add(z, "images/ok.png", Png());
            Add(z, "images/../../evil.png", Png());
            Add(z, "images/..\\evil2.png", Png());
            Add(z, "../outside.png", Png());
            Add(z, "images/sub/dir.png", Png());
            Add(z, "images/.hidden.png", Png());
        });
        var m = Machine("b");

        var preview = m.Packages.Inspect(zip);

        Assert.Equal(new[] { "ok.png" }, preview.Images.Keys);
        Assert.Equal(2, preview.MissingPictures);                      // the two hostile paths were dropped from the profile
        m.Packages.Commit(preview);
        Assert.False(File.Exists(Path.Combine(_root, "evil.png")));
        Assert.Single(Directory.GetFiles(m.Media.Directory));
    }

    [Fact]
    public void ARenamedProgram_IsNotAPicture()
    {
        string zip = MakeZip("fake.mhdeck", z =>
        {
            Add(z, "profile.json", ProfileJson("images/fake.png"));
            Add(z, "images/fake.png", Encoding.ASCII.GetBytes("MZ this is an executable"));
        });

        var preview = Machine("b").Packages.Inspect(zip);

        Assert.Empty(preview.Images);
    }

    [Fact]
    public void ScriptsInSvg_AreRefused_PlainSvgIsKept()
    {
        string zip = MakeZip("svg.mhdeck", z =>
        {
            Add(z, "profile.json", ProfileJson("images/good.svg", "images/bad.svg"));
            Add(z, "images/good.svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"3\"/></svg>"));
            Add(z, "images/bad.svg", Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>"));
        });

        var preview = Machine("b").Packages.Inspect(zip);

        Assert.Equal(new[] { "good.svg" }, preview.Images.Keys);
    }

    [Fact]
    public void TooManyEntries_Oversize_AndBombs_AreRejected()
    {
        string many = MakeZip("many.mhdeck", z =>
        {
            Add(z, "profile.json", ProfileJson());
            for (int i = 0; i < ProfilePackageService.MaxEntries; i++) Add(z, $"junk/{i}.txt", new byte[] { 1 });   // 1001 entries
        });
        Assert.Throws<PackageException>(() => Machine("b").Packages.Inspect(many));

        string big = MakeZip("big.mhdeck", z =>
        {
            Add(z, "profile.json", ProfileJson("images/big.png"));
            var bytes = new byte[(int)ProfilePackageService.MaxPictureBytes + 1];
            bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
            Add(z, "images/big.png", bytes);
        });
        Assert.Throws<PackageException>(() => Machine("b").Packages.Inspect(big));

        // a "bomb": many pictures that together unpack to more than 100 MB (each one is within its own limit)
        string bomb = MakeZip("bomb.mhdeck", z =>
        {
            Add(z, "profile.json", ProfileJson());
            for (int i = 0; i < 8; i++)
            {
                var bytes = new byte[14 * 1024 * 1024];
                bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47; bytes[4] = (byte)i;
                Add(z, $"images/p{i}.png", bytes);
            }
        });
        Assert.Throws<PackageException>(() => Machine("b").Packages.Inspect(bomb));
    }

    [Fact]
    public void NotAZip_NoProfile_AndBrokenJson_AreRejectedClearly()
    {
        string notZip = Path.Combine(_root, "text.mhdeck");
        Directory.CreateDirectory(_root);
        File.WriteAllText(notZip, "hello");
        Assert.Throws<PackageException>(() => Machine("b").Packages.Inspect(notZip));

        string noProfile = MakeZip("noprofile.mhdeck", z => Add(z, "images/a.png", Png()));
        Assert.Throws<PackageException>(() => Machine("b").Packages.Inspect(noProfile));

        string broken = MakeZip("broken.mhdeck", z => Add(z, "profile.json", Encoding.UTF8.GetBytes("{ not json")));
        Assert.Throws<PackageException>(() => Machine("b").Packages.Inspect(broken));
    }

    [Fact]
    public void UnknownEntries_AreIgnored()
    {
        string zip = MakeZip("extra.mhdeck", z =>
        {
            Add(z, "profile.json", ProfileJson());
            Add(z, "sounds/boom.wav", new byte[] { 1, 2 });
            Add(z, "README.txt", new byte[] { 1 });
            Add(z, "tool.exe", new byte[] { 0x4D, 0x5A });
        });

        var preview = Machine("b").Packages.Inspect(zip);

        Assert.Empty(preview.Images);
    }

    // ── programs ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProgramScan_ListsEverythingThatCanExecute_IncludingGesturesAndCommands()
    {
        var profile = new ProfileModel
        {
            Pages = new List<KeyPageModel>
            {
                new()
                {
                    PageIndex = 0,
                    Keys = new List<KeyAssignmentModel>
                    {
                        new() { Index = 0, Action = DeckActionType.OpenApplication, ActionParameter = @"C:\Tools\x.exe" },
                        new() { Index = 1, Action = DeckActionType.MediaNext },   // harmless
                        new() { Index = 2, Action = DeckActionType.MediaStop, Gestures = new KeyGestures
                            { Hold = new KeyGestureBinding { Action = DeckActionType.OpenWebsite, ActionParameter = "https://evil.test" } } },
                        new() { Index = 3, Action = DeckActionType.LiveValue, LiveValue = new LiveValueSettings { Kind = LiveValueSourceKind.Command, Command = "del /q *" } },
                        new() { Index = 4, Action = DeckActionType.TextPaste, ActionParameter = "powershell -c ..." },
                        new() { Index = 5, Action = DeckActionType.ConditionalAction, ActionParameter = new ConditionalActionModel
                            { Condition = ConditionType.FileExists, Target = "x", IfTrueAction = DeckActionType.OpenFile, IfTrueParameter = "a.bat" }.ToParameterString() }
                    }
                }
            },
            Folders = new List<KeyFolderModel>
            {
                new() { Name = "Tools", Keys = new List<KeyAssignmentModel> { new() { Index = 1, Action = DeckActionType.MacroPlay, ActionParameter = "id" } } }
            }
        };

        var risks = ProgramScanner.Scan(profile);

        Assert.Contains(risks, r => r.Key == 1 && r.Action == DeckActionType.OpenApplication && r.Detail.Contains("x.exe"));
        Assert.Contains(risks, r => r.Key == 3 && r.Action == DeckActionType.OpenWebsite);   // inside a hold gesture
        Assert.Contains(risks, r => r.Key == 4 && r.Detail.StartsWith("command:"));
        Assert.Contains(risks, r => r.Key == 5 && r.Action == DeckActionType.TextPaste);
        Assert.Contains(risks, r => r.Key == 6 && r.Action == DeckActionType.ConditionalAction);
        Assert.Contains(risks, r => r.Page == "Tools" && r.Action == DeckActionType.MacroPlay);
        Assert.DoesNotContain(risks, r => r.Key == 2 && r.Action == DeckActionType.MediaNext);
    }

    [Fact]
    public void ProgramScan_Package_IsReportedBeforeAnythingIsSaved()
    {
        var a = Machine("a");
        var profile = a.Profiles.CreateProfile("Tools", setAsActive: false);
        profile.Keys = new List<KeyAssignmentModel> { new() { Index = 0, Action = DeckActionType.OpenApplication, ActionParameter = "calc.exe" } };
        a.Profiles.SaveProfile(profile);
        string package = Path.Combine(_root, "tools.mhdeck");
        a.Packages.Export(profile.Id, package);

        var b = Machine("b");
        int before = b.Profiles.GetAllProfiles().Count;
        var preview = b.Packages.Inspect(package);

        Assert.Single(preview.Risks);
        Assert.Equal(before, b.Profiles.GetAllProfiles().Count);          // nothing saved yet
    }

    [Fact]
    public void LegacyExternalPicture_IsImportedIntoTheStore_OnExport()
    {
        var a = Machine("a");
        var profile = a.Profiles.CreateProfile("Legacy", setAsActive: false);
        string external = Picture("legacy.png", 9);                          // an old profile points at a file outside the store
        profile.Keys = new List<KeyAssignmentModel> { new() { Index = 0, Action = DeckActionType.MediaNext, ImagePath = external } };
        a.Profiles.SaveProfile(profile);

        string package = Path.Combine(_root, "legacy.mhdeck");
        a.Packages.Export(profile.Id, package);

        using var zip = ZipFile.OpenRead(package);
        Assert.Single(zip.Entries, e => e.FullName.StartsWith("images/"));
    }
}

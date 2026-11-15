using System.Text.Json.Nodes;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Backup;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

/// <summary>T-02: snapshots, restore, retention, corrupt-file quarantine and the schema-version migration chain.</summary>
public sealed class BackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_backup_" + Guid.NewGuid().ToString("N"));

    private sealed class PlainProtection : ISecretProtectionService
    {
        public bool IsProtected(string? value) => false;
        public string? Protect(string? plainText) => plainText;
        public string? Unprotect(string? cipherText) => cipherText;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private ProfileService Create(ProfileMigrator? migrator = null) => new(_dir, new PlainProtection(), migrator);

    private string ProfilePath(string id) => Path.Combine(_dir, "profiles", id + ".json");

    // ── snapshots ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_ThenRestore_RoundTripsAndDropsLaterProfiles()
    {
        var svc = Create();
        var keep = svc.CreateProfile("Keep", setAsActive: false);

        var snap = svc.Backups.CreateSnapshot("manual");
        Assert.NotNull(snap);
        Assert.Equal("manual", snap!.Reason);
        Assert.Equal(2, snap.ProfileCount); // default + Keep

        var later = svc.CreateProfile("Later", setAsActive: false);
        keep.Name = "Changed";
        svc.SaveProfile(keep);

        Assert.True(svc.RestoreBackup(snap.Name));

        var all = svc.GetAllProfiles();
        Assert.DoesNotContain(all, p => p.Id == later.Id);
        Assert.Equal("Keep", all.Single(p => p.Id == keep.Id).Name);
    }

    [Fact]
    public void Restore_TakesBeforeRestoreSnapshot_SoItCanBeUndone()
    {
        var svc = Create();
        var snap = svc.Backups.CreateSnapshot("manual")!;
        var added = svc.CreateProfile("Added after", setAsActive: false);

        Assert.True(svc.RestoreBackup(snap.Name));
        Assert.DoesNotContain(svc.GetAllProfiles(), p => p.Id == added.Id);

        var undo = svc.Backups.List().First(b => b.Reason == "before-restore");
        Assert.True(svc.RestoreBackup(undo.Name));
        Assert.Contains(svc.GetAllProfiles(), p => p.Id == added.Id);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\profiles")]
    [InlineData("../profiles")]
    [InlineData("a/b")]
    [InlineData("C:\\Windows")]
    [InlineData("")]
    [InlineData("does-not-exist")]
    public void Restore_RejectsUnsafeOrUnknownNames(string name)
    {
        var svc = Create();
        svc.Backups.CreateSnapshot("manual");
        Assert.False(svc.RestoreBackup(name));
        Assert.False(svc.Backups.Delete(name));
        Assert.True(File.Exists(ProfilePath("default")));
    }

    [Fact]
    public void Restore_IgnoresUnsafeFileNamesInsideASnapshot()
    {
        var svc = Create();
        var snap = svc.Backups.CreateSnapshot("manual")!;
        File.WriteAllText(Path.Combine(snap.FullPath, "profiles", "bad name.json"), "{}");

        Assert.True(svc.RestoreBackup(snap.Name));
        Assert.False(File.Exists(Path.Combine(_dir, "profiles", "bad name.json")));
    }

    [Fact]
    public void Snapshots_SameSecond_GetUniqueFolders()
    {
        var svc = Create();
        var a = svc.Backups.CreateSnapshot("manual")!;
        var b = svc.Backups.CreateSnapshot("manual")!;
        Assert.NotEqual(a.Name, b.Name);
        Assert.Equal("manual", b.Reason); // counter suffix is not part of the reason
    }

    [Fact]
    public void Reason_IsSanitisedAndShortened()
    {
        Assert.Equal("a-b-c", BackupService.SanitizeReason("a/b\\c"));
        Assert.Equal(30, BackupService.SanitizeReason(new string('x', 80)).Length);
        Assert.Equal("manual", BackupService.SanitizeReason("   "));
    }

    [Fact]
    public void Retention_KeepsOnlyNewest30()
    {
        var svc = Create();
        for (int i = 0; i < BackupService.MaxSnapshots + 5; i++)
        {
            Assert.NotNull(svc.Backups.CreateSnapshot("manual"));
        }
        Assert.Equal(BackupService.MaxSnapshots, svc.Backups.List().Count);
    }

    [Fact]
    public void DailySnapshot_OnlyWhenNewestIsOlderThan24Hours()
    {
        var clock = new DateTime(2026, 11, 13, 9, 0, 0);
        Directory.CreateDirectory(Path.Combine(_dir, "profiles"));
        File.WriteAllText(ProfilePath("default"), """{"Id":"default","Name":"x","IsDefault":true}""");

        var backups = new BackupService(_dir, () => clock);
        Assert.NotNull(backups.EnsureDailySnapshot());          // none yet
        clock = clock.AddHours(23);
        Assert.Null(backups.EnsureDailySnapshot());             // younger than 24 h
        clock = clock.AddHours(2);
        var second = backups.EnsureDailySnapshot();             // 25 h after the first
        Assert.NotNull(second);
        Assert.Equal("daily", second!.Reason);
    }

    [Fact]
    public void Snapshot_WithNothingToBackUp_ReturnsNull()
    {
        Assert.Null(new BackupService(_dir).CreateSnapshot("manual"));
    }

    [Fact]
    public void Snapshot_IsMirroredToTheConfiguredFolder()
    {
        string mirror = Path.Combine(_dir, "mirror");
        Directory.CreateDirectory(Path.Combine(_dir, "profiles"));
        File.WriteAllText(ProfilePath("default"), """{"Id":"default"}""");

        var snap = new BackupService(_dir, mirrorPath: () => mirror).CreateSnapshot("manual")!;

        Assert.True(File.Exists(Path.Combine(mirror, snap.Name, "profiles", "default.json")));
    }

    [Fact]
    public void DeleteAndImport_TakeSnapshotsFirst()
    {
        var svc = Create();
        var p = svc.CreateProfile("Doomed", setAsActive: false);
        svc.DeleteProfile(p.Id);
        Assert.Contains(svc.Backups.List(), b => b.Reason == "before-delete");

        svc.ImportProfileFromJson("""{"Id":"imp1","Name":"Imported"}""");
        Assert.Contains(svc.Backups.List(), b => b.Reason == "before-import");

        // the deleted profile can be brought back from the "before-delete" snapshot
        var snap = svc.Backups.List().First(b => b.Reason == "before-delete");
        Assert.True(svc.RestoreBackup(snap.Name));
        Assert.Contains(svc.GetAllProfiles(), x => x.Id == p.Id);
    }

    // ── corrupt files ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void CorruptProfile_IsQuarantinedNotDeleted_AndOthersStillLoad()
    {
        var svc = Create();
        var good = svc.CreateProfile("Good", setAsActive: false);
        File.WriteAllText(ProfilePath("broken"), "{ this is not json");

        var all = svc.GetAllProfiles();

        Assert.Contains(all, p => p.Id == good.Id);
        Assert.False(File.Exists(ProfilePath("broken")));
        var quarantined = Assert.Single(svc.QuarantinedFiles);
        Assert.Contains(".corrupt-", quarantined);
        Assert.Equal("{ this is not json", File.ReadAllText(quarantined));

        // a second read does not quarantine again
        svc.GetAllProfiles();
        Assert.Single(svc.QuarantinedFiles);
    }

    [Fact]
    public void CorruptSettings_IsQuarantined_AndNotOverwrittenSilently()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "\0\0 garbage");

        var svc = Create();
        svc.SaveAppSettings(new AppSettingsModel { LanguageCode = "en" });

        Assert.Contains(svc.QuarantinedFiles, f => f.Contains("settings.json.corrupt-"));
        Assert.Equal("\0\0 garbage", File.ReadAllText(svc.QuarantinedFiles.First(f => f.Contains("settings.json.corrupt-"))));
        Assert.Equal("en", Create().GetAppSettings().LanguageCode);
    }

    [Fact]
    public void LockedProfile_IsSkippedNotQuarantined()
    {
        var svc = Create();
        var p = svc.CreateProfile("Locked", setAsActive: false);

        using (new FileStream(ProfilePath(p.Id), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.DoesNotContain(svc.GetAllProfiles(), x => x.Id == p.Id);
        }

        Assert.Empty(svc.QuarantinedFiles);
        Assert.Contains(svc.GetAllProfiles(), x => x.Id == p.Id);
    }

    // ── schema version & migrations ────────────────────────────────────────────────────────

    private sealed class RenameKeyMigration : IProfileMigration
    {
        public int FromVersion => 1;

        public void Migrate(JsonObject profile)
        {
            // frozen v1 fixture used a field called "Title"; v2 calls it "Name"
            if (profile.Remove("Title", out var title)) profile["Name"] = title;
        }
    }

    [Fact]
    public void MissingSchemaVersion_IsVersion1_AndNewProfilesAreStamped()
    {
        Assert.Equal(1, ProfileMigrator.ReadVersion(new JsonObject { ["Id"] = "x" }));

        var svc = Create();
        var json = JsonNode.Parse(File.ReadAllText(ProfilePath("default")))!.AsObject();
        Assert.Equal(ProfileMigrator.CurrentVersion, (int)json["SchemaVersion"]!);
    }

    [Fact]
    public void Migration_RunsOnLoad_AfterASnapshot_AndRewritesTheFile()
    {
        // 1) a "v1" profile written by an older app: no SchemaVersion, old field name
        Directory.CreateDirectory(Path.Combine(_dir, "profiles"));
        File.WriteAllText(ProfilePath("old1"), """{"Id":"old1","Title":"From the past","IsDefault":true}""");

        // 2) the app now targets schema 2
        var migrator = new ProfileMigrator(new IProfileMigration[] { new RenameKeyMigration() }, targetVersion: 2);
        var svc = Create(migrator);

        var loaded = svc.GetAllProfiles().Single(p => p.Id == "old1");
        Assert.Equal("From the past", loaded.Name);
        Assert.Equal(2, loaded.SchemaVersion);

        var onDisk = JsonNode.Parse(File.ReadAllText(ProfilePath("old1")))!.AsObject();
        Assert.Equal(2, (int)onDisk["SchemaVersion"]!);
        Assert.False(onDisk.ContainsKey("Title"));

        // the snapshot holds the untouched v1 file
        var snap = svc.Backups.List().First(b => b.Reason == "before-migration");
        string original = File.ReadAllText(Path.Combine(snap.FullPath, "profiles", "old1.json"));
        Assert.Contains("\"Title\"", original);
    }

    [Fact]
    public void Migrator_ThrowsWhenAStepIsMissing()
    {
        var migrator = new ProfileMigrator(Array.Empty<IProfileMigration>(), targetVersion: 3);
        Assert.Throws<InvalidOperationException>(() => migrator.Apply(new JsonObject()));
    }
}

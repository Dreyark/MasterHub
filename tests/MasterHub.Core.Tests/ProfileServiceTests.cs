using System.Text.Json;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_tests_" + Guid.NewGuid().ToString("N"));

    /// <summary>Deterministyczna "kryptografia" - pozwala sprawdzić, że na dysk nie trafia tekst jawny.</summary>
    private sealed class FakeProtection : ISecretProtectionService
    {
        public bool IsProtected(string? value) => value?.StartsWith("enc:v1:") == true;

        public string? Protect(string? plainText) =>
            string.IsNullOrEmpty(plainText) || IsProtected(plainText)
                ? plainText
                : "enc:v1:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));

        public string? Unprotect(string? cipherText) =>
            IsProtected(cipherText)
                ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cipherText!["enc:v1:".Length..]))
                : cipherText;
    }

    private ProfileService Create() => new(_dir, new FakeProtection());

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("a1b2c3d4e5f60718293a4b5c6d7e8f90", true)]
    [InlineData("my-profile_2", true)]
    [InlineData("", false)]
    [InlineData("..\\settings", false)]
    [InlineData("../settings", false)]
    [InlineData("a/b", false)]
    [InlineData("C:\\evil", false)]
    [InlineData("name.with.dots", false)]
    public void IsSafeProfileId_AcceptsOnlySafeFileNames(string id, bool expected)
    {
        Assert.Equal(expected, ProfileService.IsSafeProfileId(id));
    }

    [Fact]
    public void Import_WithTraversalId_IsSavedUnderSafeIdInsideProfilesDir()
    {
        var svc = Create();
        var json = JsonSerializer.Serialize(new ProfileModel { Id = "..\\..\\settings", Name = "Evil" });

        var imported = svc.ImportProfileFromJson(json);

        Assert.True(ProfileService.IsSafeProfileId(imported.Id));
        Assert.True(File.Exists(Path.Combine(_dir, "profiles", imported.Id + ".json")));

        var settingsPath = Path.Combine(_dir, "settings.json");
        if (File.Exists(settingsPath))
            Assert.DoesNotContain("\"Evil\"", File.ReadAllText(settingsPath));
    }

    [Fact]
    public void SaveProfile_RejectsUnsafeId()
    {
        var svc = Create();
        Assert.Throws<ArgumentException>(() => svc.SaveProfile(new ProfileModel { Id = "..\\x", Name = "x" }));
    }

    [Fact]
    public void SaveProfile_EncryptsCredentialsOnDisk_AndRoundTrips()
    {
        var svc = Create();
        var prof = svc.CreateProfile("Creds");
        prof.ObsPassword = "S3cret-OBS";
        prof.TwitchOAuthToken = "oauth:tok";
        svc.SaveProfile(prof);

        string onDisk = File.ReadAllText(Path.Combine(_dir, "profiles", prof.Id + ".json"));
        Assert.DoesNotContain("S3cret-OBS", onDisk);
        Assert.DoesNotContain("oauth:tok", onDisk);

        var loaded = svc.GetAllProfiles().Single(p => p.Id == prof.Id);
        Assert.Equal("S3cret-OBS", loaded.ObsPassword);
        Assert.Equal("oauth:tok", loaded.TwitchOAuthToken);
    }

    [Fact]
    public void Export_StripsAllCredentials()
    {
        var svc = Create();
        var prof = svc.CreateProfile("Exp");
        prof.ObsPassword = "pw";
        prof.YouTubeApiKey = "key";
        prof.DiscordAccessToken = "dc";
        svc.SaveProfile(prof);

        string json = svc.ExportProfileToJson(prof.Id);

        Assert.DoesNotContain("\"pw\"", json);
        Assert.DoesNotContain("\"key\"", json);
        Assert.DoesNotContain("\"dc\"", json);
    }

    [Fact]
    public void DefaultProfile_KeepsUserChosenCyanColor_AcrossRestarts()
    {
        // Regresja: dawna "migracja" zerowała kolor #00FFCC profilu domyślnego przy każdym starcie.
        var svc = Create();
        var def = svc.GetActiveProfile();
        def.LedColorHex = "#00FFCC";
        def.BaseBrightness = 80;
        svc.SaveProfile(def);

        var reopened = Create();
        var again = reopened.GetAllProfiles().Single(p => p.Id == def.Id);

        Assert.Equal("#00FFCC", again.LedColorHex);
        Assert.Equal(80, again.BaseBrightness);
    }

    [Fact]
    public void Settings_RoundTripWithEncryptedSecrets()
    {
        var svc = Create();
        svc.SaveObsSettings(new ObsSettingsModel { Host = "h", Port = 4455, Password = "obs-pass" });

        Assert.DoesNotContain("obs-pass", File.ReadAllText(Path.Combine(_dir, "settings.json")));
        Assert.Equal("obs-pass", Create().GetObsSettings().Password);
    }

    [Fact]
    public void DeleteProfile_WithTraversalId_DoesNothing()
    {
        var svc = Create();
        string outside = Path.Combine(_dir, "keep.json");
        File.WriteAllText(outside, "{}");

        svc.DeleteProfile("..\\keep");

        Assert.True(File.Exists(outside));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Check;
using Xunit;

namespace MasterHub.Core.Tests;

/// <summary>T-34 #9: the import / profile check lists what a profile points at that this PC lacks.</summary>
public class ProfileCheckTests
{
    private sealed class FakeEnv : IProfileCheckEnvironment
    {
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool FileExists(string path) => Files.Contains(path);
        public bool DirectoryExists(string path) => Dirs.Contains(path);
        public bool BareNameResolves(string name) => Names.Contains(name);
        public IReadOnlyList<AudioDeviceInfo>? AudioDevices { get; set; }
        public IReadOnlyCollection<string>? ObsScenes { get; set; }
        public IReadOnlyCollection<string>? HomeAssistantEntities { get; set; }
        public IReadOnlyCollection<string>? InstalledPlugins { get; set; }
    }

    private static ProfileModel Profile(params KeyAssignmentModel[] keys) => new() { Name = "P", Keys = keys.ToList() };

    private static KeyAssignmentModel Key(int index, DeckActionType action, string parameter = "") =>
        new() { Index = index, Action = action, ActionParameter = parameter };

    [Fact]
    public void ProgramAndFilePaths_ThatAreMissing_AreMarked()
    {
        var env = new FakeEnv();
        env.Files.Add(@"C:\Apps\here.exe");
        env.Dirs.Add(@"D:\Docs");

        var findings = ProfileCheck.Run(Profile(
            Key(0, DeckActionType.OpenApplication, @"C:\Apps\here.exe"),
            Key(1, DeckActionType.OpenApplication, @"C:\Apps\gone.exe"),
            Key(2, DeckActionType.OpenFile, @"D:\Docs"),
            Key(3, DeckActionType.OpenFile, @"D:\Docs\gone.txt")), env);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Key == 2 && f.Problem == CheckProblem.MissingProgram);
        Assert.Contains(findings, f => f.Key == 4 && f.Problem == CheckProblem.MissingFile);
    }

    [Fact]
    public void ABareName_IsMarked_OnlyWhenItOpensNothing()
    {
        var env = new FakeEnv();
        env.Names.Add("notepad");

        var findings = ProfileCheck.Run(Profile(
            Key(0, DeckActionType.OpenApplication, "notepad"),
            Key(1, DeckActionType.OpenApplication, "someapp"),
            Key(2, DeckActionType.OpenApplication, "steam://rungameid/570"),          // a link: cannot be checked, not marked
            Key(3, DeckActionType.OpenApplication, @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")), env);

        var mark = Assert.Single(findings);
        Assert.Equal(CheckProblem.BareNameNotFound, mark.Problem);
        Assert.Equal("someapp", mark.Target);
    }

    [Fact]
    public void Pictures_OfKeysAndTheWallpaper_AreChecked()
    {
        var env = new FakeEnv();
        env.Files.Add(@"C:\pics\ok.png");
        var key = Key(0, DeckActionType.MediaNext);
        key.ImagePath = @"C:\pics\ok.png";
        key.ActiveImagePath = @"C:\pics\gone.png";
        var profile = Profile(key);
        profile.PanoramicWallpaperPath = @"C:\pics\wall.gif";

        var findings = ProfileCheck.Run(profile, env);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(CheckProblem.MissingPicture, f.Problem));
        Assert.Contains(findings, f => f.Key == 0 && f.Target.EndsWith("wall.gif"));
    }

    [Fact]
    public void Integrations_AreChecked_OnlyWhenTheAppCanSeeThem()
    {
        var keys = new[]
        {
            Key(0, DeckActionType.ObsSceneSwitch, "Gaming"),
            Key(1, DeckActionType.HomeAssistantToggle, "light.desk"),
            new KeyAssignmentModel { Index = 2, Action = DeckActionType.StreamDeckPluginAction, StreamDeckPluginUuid = "com.example.plugin" }
        };

        // OBS not connected, HA not set up, plugins unknown: nothing is marked
        Assert.Empty(ProfileCheck.Run(Profile(keys), new FakeEnv()));

        var env = new FakeEnv
        {
            ObsScenes = new[] { "Just Chatting" },
            HomeAssistantEntities = new[] { "light.kitchen" },
            InstalledPlugins = new[] { "com.other.plugin" }
        };
        var findings = ProfileCheck.Run(Profile(keys), env);

        Assert.Equal(new[] { CheckProblem.MissingObsScene, CheckProblem.UnknownHaEntity, CheckProblem.MissingPlugin },
            findings.Select(f => f.Problem));
    }

    [Fact]
    public void AudioSwitchKeys_FindDevicesByName_WhenTheIdChanged()
    {
        var env = new FakeEnv
        {
            AudioDevices = new[]
            {
                new AudioDeviceInfo("{new-id}", "2 - Speakers (Realtek(R) Audio)", false, true),
                new AudioDeviceInfo("{mic}", "Microphone", true, true)
            }
        };
        string parameter = (AudioSwitchParameter.Empty() with
        {
            A = new SavedAudioDevice("{old-id}", "Speakers (Realtek(R) Audio)"),
            B = new SavedAudioDevice("{gone}", "Headphones (USB)")
        }).Format();

        var findings = ProfileCheck.Run(Profile(Key(0, DeckActionType.AudioOutputSwitch, parameter)), env);

        var mark = Assert.Single(findings);
        Assert.Equal(CheckProblem.MissingAudioDevice, mark.Problem);
        Assert.Equal("Headphones (USB)", mark.Target);
    }

    [Fact]
    public void SoundKeys_CheckTheFile_AndTheOutputDevice()
    {
        var env = new FakeEnv { AudioDevices = new[] { new AudioDeviceInfo("{cable}", "CABLE Input", false, false) } };
        var key = Key(0, DeckActionType.SoundPlay);
        key.Sound = new SoundSettings { FilePath = @"C:\sfx\horn.mp3", DeviceId = "{gone}" };

        var findings = ProfileCheck.Run(Profile(key), env);

        Assert.Equal(new[] { CheckProblem.MissingSound, CheckProblem.MissingAudioDevice }, findings.Select(f => f.Problem));
    }

    [Fact]
    public void Gestures_AndConditionalBranches_AreChecked_AndEachMarkIsListedOnce()
    {
        var key = Key(0, DeckActionType.OpenApplication, @"C:\x\gone.exe");
        key.Gestures = new KeyGestures { Hold = new KeyGestureBinding { Action = DeckActionType.OpenFile, ActionParameter = @"C:\x\gone.txt" } };
        var profile = Profile(key);
        profile.Pages = new List<KeyPageModel> { new() { Keys = new List<KeyAssignmentModel> { key } } };   // page 1 mirrors the top-level keys

        var findings = ProfileCheck.Run(profile, new FakeEnv());

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Gesture == "hold" && f.Problem == CheckProblem.MissingFile);
    }

    [Fact]
    public void ACleanProfile_HasNoMarks_AndTheProfileIsNotChanged()
    {
        var key = Key(0, DeckActionType.MediaPlayPause);
        var profile = Profile(key);
        string before = System.Text.Json.JsonSerializer.Serialize(profile);

        Assert.Empty(ProfileCheck.Run(profile, new FakeEnv()));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(profile));
    }

    [Fact]
    public void TheSystemEnvironment_ResolvesBareNames_FromInstalledAppsAndAppPaths()
    {
        var env = new SystemProfileCheckEnvironment(new[] { "Spotify.exe", "Discord" }, exe => exe.Equals("special.exe", StringComparison.OrdinalIgnoreCase));

        Assert.True(env.BareNameResolves("spotify"));
        Assert.True(env.BareNameResolves("discord.exe"));
        Assert.True(env.BareNameResolves("special"));
        Assert.True(env.BareNameResolves("cmd"));   // on PATH (System32)
        Assert.False(env.BareNameResolves("definitely-not-a-program-xyz"));
        Assert.False(env.BareNameResolves(" "));
    }
}

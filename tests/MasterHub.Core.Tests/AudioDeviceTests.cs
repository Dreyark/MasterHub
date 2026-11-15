using MasterHub.Core.Audio;

namespace MasterHub.Core.Tests;

/// <summary>T-16: name normalisation, matching a saved device by id or (renumbered) name, and the "toggle between two devices" plan.</summary>
public class AudioDeviceTests
{
    private static AudioDeviceInfo Dev(string id, string name, bool isDefault = false, bool capture = false) =>
        new(id, name, capture, isDefault);

    [Theory]
    [InlineData("2 - Realtek(R) Audio", "Realtek(R) Audio")]
    [InlineData("3- USB Audio", "USB Audio")]
    [InlineData("  10 -  Headphones (WH-1000XM4)  ", "Headphones (WH-1000XM4)")]
    [InlineData("Speakers (Realtek(R) Audio)", "Speakers (Realtek(R) Audio)")]
    [InlineData("1 Monitor", "1 Monitor")]            // a number without the dash is part of the name
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_StripsOnlyWindowsDeviceNumber(string? input, string expected)
    {
        Assert.Equal(expected, AudioDeviceNames.Normalize(input));
    }

    [Fact]
    public void SameName_IgnoresNumberAndCase()
    {
        Assert.True(AudioDeviceNames.SameName("2 - Realtek(R) Audio", "realtek(r) audio"));
        Assert.False(AudioDeviceNames.SameName("Realtek(R) Audio", "USB Audio"));
    }

    [Fact]
    public void Resolve_PrefersTheId_ThenFallsBackToTheNameWithoutItsNumber()
    {
        var devices = new[]
        {
            Dev("id-1", "Speakers (Realtek(R) Audio)"),
            Dev("id-2", "3 - Headphones (USB Audio)")        // Windows renumbered it: it was "2 - Headphones…"
        };

        Assert.Equal("id-1", AudioDeviceMatcher.Resolve(devices, new SavedAudioDevice("id-1", "anything"))!.Id);
        Assert.Equal("id-2", AudioDeviceMatcher.Resolve(devices, new SavedAudioDevice("id-gone", "2 - Headphones (USB Audio)"))!.Id);
        Assert.Null(AudioDeviceMatcher.Resolve(devices, new SavedAudioDevice("id-gone", "Bluetooth Speaker")));
        Assert.Null(AudioDeviceMatcher.Resolve(devices, null));
        Assert.Null(AudioDeviceMatcher.Resolve(devices, new SavedAudioDevice("", "")));
    }

    [Fact]
    public void Parameter_RoundTrips_WithSpecialCharactersInNames()
    {
        var p = new AudioSwitchParameter(Capture: false,
            new SavedAudioDevice("{0.0.0.00000000}.{aaa-111}", "Speakers; (Realtek | Audio) ~ ü"),
            new SavedAudioDevice("{0.0.0.00000000}.{bbb-222}", "Headphones"));

        var back = AudioSwitchParameter.Parse(p.Format());

        Assert.Equal(p, back);
        Assert.True(back.IsComplete);
        Assert.False(back.Capture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("render;only-one")]
    [InlineData("loud;a|b;c|d")]
    public void Parameter_Parse_NeverThrows_AndIsIncomplete(string text)
    {
        var p = AudioSwitchParameter.Parse(text);
        Assert.False(p.IsComplete);
    }

    // ── planning ───────────────────────────────────────────────────────────────────────────

    private static AudioSwitchParameter Param(bool capture = false) =>
        new(capture, new SavedAudioDevice("A", "Speakers"), new SavedAudioDevice("B", "Headphones"));

    [Fact]
    public void Plan_TogglesBetweenTheTwoDevices()
    {
        var onA = new[] { Dev("A", "Speakers", isDefault: true), Dev("B", "Headphones") };
        var onB = new[] { Dev("A", "Speakers"), Dev("B", "Headphones", isDefault: true) };

        Assert.Equal("B", AudioSwitchPlanner.Plan(onA, Param()).Target!.Id);
        Assert.Equal("A", AudioSwitchPlanner.Plan(onB, Param()).Target!.Id);
    }

    [Fact]
    public void Plan_WhenNeitherIsTheDefault_GoesToTheFirst()
    {
        var other = new[] { Dev("A", "Speakers"), Dev("B", "Headphones"), Dev("C", "HDMI", isDefault: true) };
        Assert.Equal("A", AudioSwitchPlanner.Plan(other, Param()).Target!.Id);
    }

    [Fact]
    public void Plan_MissingDevice_GivesAClearProblem_NotAnException()
    {
        var onlyA = new[] { Dev("A", "Speakers") };
        var plan = AudioSwitchPlanner.Plan(onlyA, Param());
        Assert.Equal(AudioSwitchProblem.SecondMissing, plan.Problem);
        Assert.Equal("A", plan.Target!.Id); // the one that exists is still reachable

        var onlyADefault = new[] { Dev("A", "Speakers", isDefault: true) };
        var stuck = AudioSwitchPlanner.Plan(onlyADefault, Param());
        Assert.Equal(AudioSwitchProblem.SecondMissing, stuck.Problem);
        Assert.False(stuck.CanSwitch);

        Assert.Equal(AudioSwitchProblem.BothMissing, AudioSwitchPlanner.Plan(Array.Empty<AudioDeviceInfo>(), Param()).Problem);
        Assert.Equal(AudioSwitchProblem.FirstMissing, AudioSwitchPlanner.Plan(new[] { Dev("B", "Headphones", isDefault: true) }, Param()).Problem);
    }

    [Fact]
    public void Plan_SurvivesRenumbering_ByName()
    {
        var p = new AudioSwitchParameter(false, new SavedAudioDevice("old-a", "2 - Realtek(R) Audio"), new SavedAudioDevice("old-b", "Headphones"));
        var devices = new[] { Dev("new-a", "4 - Realtek(R) Audio", isDefault: true), Dev("new-b", "Headphones") };

        var plan = AudioSwitchPlanner.Plan(devices, p);

        Assert.Equal("new-b", plan.Target!.Id);
        Assert.Equal(AudioSwitchProblem.None, plan.Problem);
    }

    [Fact]
    public void Plan_NotConfigured_SameDevice_AndWrongKind()
    {
        Assert.Equal(AudioSwitchProblem.NotConfigured, AudioSwitchPlanner.Plan(new[] { Dev("A", "x") }, AudioSwitchParameter.Empty()).Problem);

        var same = new AudioSwitchParameter(false, new SavedAudioDevice("A", "Speakers"), new SavedAudioDevice("A", "Speakers"));
        Assert.Equal(AudioSwitchProblem.SameDevice, AudioSwitchPlanner.Plan(new[] { Dev("A", "Speakers") }, same).Problem);

        // output devices are ignored when the key switches microphones
        var devices = new[] { Dev("A", "Speakers"), Dev("B", "Headphones", isDefault: true) };
        Assert.Equal(AudioSwitchProblem.BothMissing, AudioSwitchPlanner.Plan(devices, Param(capture: true)).Problem);
    }

    [Fact]
    public void Plan_Microphones_AreSwitchedLikeSpeakers()
    {
        var mics = new[] { Dev("A", "Speakers", capture: true, isDefault: true), Dev("B", "Headphones", capture: true) };
        Assert.Equal("B", AudioSwitchPlanner.Plan(mics, Param(capture: true)).Target!.Id);
    }

    [Fact]
    public void WindowsAudioService_DoesNotCacheTheDefaultEndpoint()
    {
        // B-19: a volume knob kept controlling the old speakers because it cached the endpoint. Ours resolves the
        // default device on every call – guard the source so nobody adds a cache without re-checking the device id.
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "MasterHub.Core", "Audio", "WindowsAudioService.cs"));
        Assert.DoesNotContain("private MMDevice ", source);
        Assert.DoesNotContain("_cachedDevice", source);
        Assert.Contains("GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)", source);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MasterHub.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

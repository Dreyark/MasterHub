using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Apps;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;

namespace MasterHub.UiTests;

/// <summary>T-01 on the Profiles page: grouping, linking apps, automatic switch that is not remembered, toggle.</summary>
public sealed class SmartProfilesPageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_smart_ui_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class PlainProtection : ISecretProtectionService
    {
        public bool IsProtected(string? value) => false;
        public string? Protect(string? plainText) => plainText;
        public string? Unprotect(string? cipherText) => cipherText;
    }

    private sealed class FakeSmart : ISmartProfileService
    {
        public bool IsEnabled { get; set; } = true;
        public int PollingIntervalMs { get; set; }
        public string? FallbackProfileId { get; set; }
        public string LastForegroundProcess => "";
        public string LastForegroundTitle => "";
        public List<SmartProfileRule> Rules { get; } = new();
        public event Action<string, string>? ForegroundWindowChanged { add { } remove { } }
        public event Action<string>? AutoSwitchProfileRequested;
        public event Action<bool>? IsEnabledChanged;

        public void UpdateRules(IEnumerable<SmartProfileRule> rules) { Rules.Clear(); Rules.AddRange(rules); }
        public void Start() { }
        public void Stop() { }
        public void CheckForegroundWindowNow() { }
        public void Dispose() { }
        public void Raise(string profileId) => AutoSwitchProfileRequested?.Invoke(profileId);
        public void Toggle(bool value) { IsEnabled = value; IsEnabledChanged?.Invoke(value); }
    }

    private (ProfileService Service, FakeSmart Smart, ProfilesPageViewModel Page, List<string> Activated) Create()
    {
        var service = new ProfileService(_dir, new PlainProtection());
        var smart = new FakeSmart();
        var page = new ProfilesPageViewModel(service, smartProfiles: smart);
        var activated = new List<string>();
        page.ProfileActivatedCallback = p => activated.Add(p.Id);
        return (service, smart, page, activated);
    }

    private static void Pump() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void Profiles_AreGrouped_HomeThenYoursThenApps()
    {
        var (service, _, page, _) = Create();
        var mine = service.CreateProfile("Mine", setAsActive: false);
        var game = service.CreateProfile("Game", setAsActive: false);
        game.SmartRules.Add(new SmartProfileRule { ProfileId = game.Id, ProcessName = "cs2" });
        service.SaveProfile(game);

        page.RefreshProfiles();

        Assert.Equal(new[] { "default", mine.Id, game.Id }, page.Profiles.Select(p => p.Id));
        Assert.Equal(3, page.Profiles.Count(p => p.ShowGroupHeader)); // one heading per group
        Assert.True(page.Profiles.Single(p => p.Id == game.Id).HasLinkedApps);
    }

    [AvaloniaFact]
    public void LinkingAnApp_SavesTheRule_FeedsTheWatcher_AndUnlinkRemovesIt()
    {
        var (service, smart, page, _) = Create();
        var target = page.Profiles.Single();

        page.OpenPickerCommand.Execute(target);
        Assert.True(page.Picker.IsOpen);
        page.Picker.ManualProcess = "obs64.exe";
        page.Picker.AddManualCommand.Execute(null);

        Assert.False(page.Picker.IsOpen);
        Assert.Equal("obs64", service.GetAllProfiles().Single().SmartRules.Single().ProcessName);
        Assert.Equal("obs64", smart.Rules.Single().ProcessName);
        Assert.Equal("default", smart.Rules.Single().ProfileId);

        page.UnlinkAppCommand.Execute(page.Profiles.Single().LinkedApps.Single());
        Assert.Empty(service.GetAllProfiles().Single().SmartRules);
        Assert.Empty(smart.Rules);
    }

    [AvaloniaFact]
    public void AutomaticSwitch_ActivatesTheProfile_ButIsNotRemembered()
    {
        var (service, smart, page, activated) = Create();
        var game = service.CreateProfile("Game", setAsActive: false);
        page.RefreshProfiles();

        smart.Raise(game.Id);
        Pump();

        Assert.Equal(new[] { game.Id }, activated);
        Assert.Equal(game.Id, service.GetActiveProfile().Id);
        Assert.True(page.Profiles.Single(p => p.Id == game.Id).IsActive);

        // a new service instance (= app restart) starts again from the last manually picked profile
        Assert.Equal("default", new ProfileService(_dir, new PlainProtection()).GetActiveProfile().Id);
    }

    [AvaloniaFact]
    public void ManualPick_BecomesTheReturnTarget_AndNeverChangesHome()
    {
        var (service, smart, page, _) = Create();
        var mine = service.CreateProfile("Mine", setAsActive: false);
        page.RefreshProfiles();

        Assert.Equal("default", smart.FallbackProfileId); // the profile loaded at start

        page.ActivateProfileCommand.Execute(page.Profiles.Single(p => p.Id == mine.Id));

        Assert.Equal(mine.Id, smart.FallbackProfileId);
        Assert.True(service.GetAllProfiles().Single(p => p.Id == "default").IsDefault);
        Assert.False(service.GetAllProfiles().Single(p => p.Id == mine.Id).IsDefault);
    }

    [AvaloniaFact]
    public void AutoSwitchToggle_IsPersisted_AndFollowsTheTray()
    {
        var (service, smart, page, _) = Create();

        page.AutoSwitchEnabled = false;
        Assert.False(smart.IsEnabled);
        Assert.False(service.GetAppSettings().AutoSwitchProfiles);

        smart.Toggle(true); // e.g. the tray menu
        Pump();
        Assert.True(page.AutoSwitchEnabled);
        Assert.True(service.GetAppSettings().AutoSwitchProfiles);
    }

    [AvaloniaFact]
    public void Picker_ListsInstalledAppsWithAKnownExecutable_AndFilters()
    {
        var installed = new FakeInstalled(
            new InstalledApp("startmenu:notepad", "Notepad", InstalledAppKind.StartMenu, @"C:\n.lnk", @"C:\Windows\notepad.exe"),
            new InstalledApp("steam:730", "Counter-Strike 2", InstalledAppKind.Steam, "steam://rungameid/730"),   // no exe: cannot be matched
            new InstalledApp("startmenu:paint", "Paint", InstalledAppKind.StartMenu, @"C:\p.lnk", @"C:\Windows\mspaint.exe"));
        var picker = new AppPickerViewModel(installed);
        picker.Open("Profile");

        Assert.Contains(picker.Items, i => i.ProcessName == "notepad");
        Assert.DoesNotContain(picker.Items, i => i.Title == "Counter-Strike 2");

        picker.Filter = "pai";
        Assert.Equal("Paint", Assert.Single(picker.Items, i => i.Title == "Paint" || i.ProcessName == "mspaint").Title);
        Assert.DoesNotContain(picker.Items, i => i.ProcessName == "notepad");
    }

    [AvaloniaFact]
    public void ProfilesPage_WithGroupsAndPicker_Renders()
    {
        var (service, _, page, _) = Create();
        service.CreateProfile("Mine", setAsActive: false);
        var game = service.CreateProfile("OBS", setAsActive: false);
        game.SmartRules.Add(new SmartProfileRule { ProfileId = game.Id, ProcessName = "obs64" });
        game.SmartRules.Add(new SmartProfileRule { ProfileId = game.Id, ProcessName = "chrome", WindowTitle = "YouTube" });
        service.SaveProfile(game);
        page.RefreshProfiles();

        Assert.True(File.Exists(Shot.Render(new ProfilesPageView { DataContext = page }, "page_profiles_smart", 1000, 760)));

        page.OpenPickerCommand.Execute(page.Profiles.Last());
        Assert.True(File.Exists(Shot.Render(new ProfilesPageView { DataContext = page }, "page_profiles_picker", 1000, 760)));
    }

    private sealed class FakeInstalled : IInstalledAppsService
    {
        public FakeInstalled(params InstalledApp[] apps) => Apps = apps;
        public IReadOnlyList<InstalledApp> Apps { get; }
        public bool IsScanning => false;
        public DateTime? LastScanUtc => null;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event Action? Changed { add { } remove { } }
    }
}

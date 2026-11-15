using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;

namespace MasterHub.UiTests;

/// <summary>T-34 #19: the tray menu lists profiles (current one ticked), switches autostart and opens the data folder.</summary>
public class TrayMenuTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_tray_" + Guid.NewGuid().ToString("N"));

    private sealed class FakeStartup : IStartupService
    {
        public bool Enabled;
        public bool IsRunAtStartupEnabled() => Enabled;
        public bool SetRunAtStartup(bool enable, bool startMinimized = true) { Enabled = enable; return true; }
        public string GetStartupExecutablePath() => "x.exe";
    }

    /// <summary>A profile store that is unreadable right now.</summary>
    public class ThrowingProfiles : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new IOException("busy");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private ProfileService Store() => new(_dir, new SecretProtectionService());

    private static NativeMenuItem Item(NativeMenu menu, int index) => (NativeMenuItem)menu.Items[index];

    private static void Click(NativeMenuItem item) => item.Command!.Execute(null);

    private static NativeMenu Build(IProfileService? profiles, Action<string>? activate, IStartupService? startup, Action<string>? folder)
    {
        var menu = new NativeMenu();
        new TrayMenuBuilder(profiles, activate, startup, folder, (_, fallback) => fallback).AddTo(menu);
        return menu;
    }

    [AvaloniaFact]
    public void TheMenu_HasProfilesAutostartAndTheDataFolder()
    {
        var menu = Build(Store(), _ => { }, new FakeStartup(), _ => { });

        Assert.Equal(3, menu.Items.Count);
        Assert.Equal("Profiles", Item(menu, 0).Header);
        Assert.Equal("Start with Windows", Item(menu, 1).Header);
        Assert.Equal("Open data folder", Item(menu, 2).Header);
    }

    [AvaloniaFact]
    public void TheProfileList_TicksTheCurrentProfile_AndSwitchesOnClick()
    {
        var store = Store();
        var work = store.CreateProfile("Work", setAsActive: true);
        var gaming = store.CreateProfile("Gaming", setAsActive: false);
        var activated = new List<string>();

        var list = Item(Build(store, activated.Add, null, _ => { }), 0).Menu!;

        var headers = list.Items.Cast<NativeMenuItem>().ToDictionary(i => i.Header!, i => i);
        Assert.True(headers["Work"].IsChecked);
        Assert.False(headers["Gaming"].IsChecked);
        Assert.Equal(MenuItemToggleType.Radio, headers["Work"].ToggleType);

        Click(headers["Gaming"]);
        Assert.Equal(new[] { gaming.Id }, activated);
        Assert.NotEqual(work.Id, gaming.Id);
    }

    [AvaloniaFact]
    public void TheProfileList_FollowsChanges_WhenTheMenuIsAboutToOpen()
    {
        var store = Store();
        store.CreateProfile("One", setAsActive: true);
        var builder = new TrayMenuBuilder(store, _ => { }, null, null, (_, f) => f);
        var list = new NativeMenu();

        builder.Refill(list);
        int before = list.Items.Count;
        var two = store.CreateProfile("Two", setAsActive: true);
        builder.Refill(list);

        Assert.Equal(before + 1, list.Items.Count);
        var ticked = list.Items.Cast<NativeMenuItem>().Single(i => i.IsChecked);
        Assert.Equal("Two", ticked.Header);
        Assert.NotNull(two);
    }

    [AvaloniaFact]
    public void ABrokenProfileStore_GivesAnEmptyList_NotACrash()
    {
        var broken = DispatchProxy.Create<IProfileService, ThrowingProfiles>();
        var builder = new TrayMenuBuilder(broken, _ => { }, null, null, (_, f) => f);
        var list = new NativeMenu();

        builder.Refill(list);

        Assert.Empty(list.Items);
    }

    [AvaloniaFact]
    public void TheAutostartEntry_SwitchesTheSetting_AndUpdatesItsTick()
    {
        var startup = new FakeStartup();
        var item = Item(Build(null, null, startup, _ => { }), 0);
        Assert.False(item.IsChecked);
        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);

        Click(item);
        Assert.True(startup.Enabled);
        Assert.True(item.IsChecked);

        Click(item);
        Assert.False(startup.Enabled);
        Assert.False(item.IsChecked);
    }

    [AvaloniaFact]
    public void TheFolderEntry_OpensTheMasterHubDataFolder()
    {
        var folders = new List<string>();
        var menu = Build(null, null, null, folders.Add);

        Click(Item(menu, menu.Items.Count - 1));

        Assert.Single(folders);
        Assert.EndsWith("MasterHub", folders[0]);
    }

    [AvaloniaFact]
    public void WithoutProfilesOrAutostart_OnlyTheFolderEntryRemains()
    {
        Assert.Single(Build(null, null, null, _ => { }).Items);
    }
}

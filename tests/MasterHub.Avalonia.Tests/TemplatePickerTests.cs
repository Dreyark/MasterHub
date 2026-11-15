using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Templates;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;

namespace MasterHub.UiTests;

/// <summary>T-10 in the Studio: the ready-made profile picker and the "send as" mode of a Hotkey key.</summary>
public sealed class TemplatePickerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_tplui_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class Plain : ISecretProtectionService
    {
        public bool IsProtected(string? value) => false;
        public string? Protect(string? plainText) => plainText;
        public string? Unprotect(string? cipherText) => cipherText;
    }

    [AvaloniaFact]
    public void Picker_ListsTemplates_PreviewsThem_AndAddsOnlyOnRequest()
    {
        var profiles = new ProfileService(_dir, new Plain());
        var page = new ProfilesPageViewModel(profiles, templates: new ProfileTemplateService(profiles));
        int before = profiles.GetAllProfiles().Count;

        page.Templates.OpenCommand.Execute(null);

        Assert.True(page.Templates.IsOpen);
        Assert.True(page.Templates.Items.Count >= 6);
        Assert.Equal(before, profiles.GetAllProfiles().Count);          // looking at templates saves nothing
        Assert.Equal(15, page.Templates.Selected!.Preview.Count);

        Assert.True(File.Exists(Shot.Render(new ProfilesPageView { DataContext = page }, "page_profiles_templates", 1100, 760)));

        page.Templates.Selected = page.Templates.Items.First(i => i.Template.Id == "obs-streaming");
        page.Templates.AddCommand.Execute(null);

        Assert.False(page.Templates.IsOpen);
        var added = profiles.GetAllProfiles().Single(p => p.Name.Contains("OBS") || p.Name.Contains("Streaming"));
        Assert.Empty(added.SmartRules);                                  // apps are not linked unless the box was ticked
        Assert.True(page.Profiles.Single(p => p.Id == added.Id).IsActive);
    }

    [AvaloniaFact]
    public void Picker_IsHidden_WithoutATemplateService()
    {
        var profiles = new ProfileService(_dir, new Plain());
        var page = new ProfilesPageViewModel(profiles);

        Assert.False(page.Templates.IsAvailable);
        page.Templates.OpenCommand.Execute(null);
        Assert.False(page.Templates.IsOpen);
    }

    [AvaloniaFact]
    public void HotkeyMode_RoundTrips_AndTheHoldOnlyShowsForGames()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.Hotkey, ActionParameter = "1" };
        Assert.True(key.IsHotkeyAction);
        Assert.False(key.ShowsHotkeyHold);

        key.HotkeySendMode = HotkeySendMode.ForGames;
        key.HotkeyHoldMs = 70;
        Assert.True(key.ShowsHotkeyHold);

        var loaded = new KeyItemViewModel { Index = 1 };
        loaded.LoadHotkeyMode(key.HotkeySendMode, key.HotkeyHoldMs);
        Assert.Equal(HotkeySendMode.ForGames, loaded.HotkeySendMode);
        Assert.Equal(70, loaded.HotkeyHoldMs);

        key.HotkeyHoldMs = 5;      // clamped to the minimum
        Assert.Equal(HotkeySender.MinHoldMs, key.HotkeyHoldMs);
        key.HotkeyHoldMs = -3;     // 0 = default
        Assert.Equal(0, key.HotkeyHoldMs);

        Assert.True(File.Exists(Shot.Render(new KeyHotkeyModeView { DataContext = key }, "inspector_hotkey_mode", 340, 300)));
    }
}

using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Check;
using MasterHub.Core.Security;

namespace MasterHub.UiTests;

/// <summary>T-34 #9 on the Profiles page: "Check" lists what the profile points at that this PC lacks.</summary>
public sealed class ProfileCheckUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_check_ui_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static Task<IProfileCheckEnvironment> EmptyPc() =>
        Task.FromResult<IProfileCheckEnvironment>(new SystemProfileCheckEnvironment(Array.Empty<string>(), _ => false));

    private ProfilesPageViewModel Create(out ProfileModel profile)
    {
        var service = new ProfileService(_dir, new SecretProtectionService());
        profile = service.GetActiveProfile();
        profile.Keys[0].Action = DeckActionType.OpenApplication;
        profile.Keys[0].ActionParameter = @"Z:\nowhere\tool.exe";
        profile.Keys[1].Action = DeckActionType.OpenFile;
        profile.Keys[1].ActionParameter = @"Z:\nowhere\notes.txt";
        service.SaveProfile(profile);
        return new ProfilesPageViewModel(service) { CheckEnvironmentFactory = EmptyPc };
    }

    [AvaloniaFact]
    public async Task Check_ShowsAMark_ForEveryMissingThing()
    {
        var page = Create(out _);
        var item = page.Profiles.First(p => p.IsActive);

        await page.CheckProfileCommand.ExecuteAsync(item);

        Assert.True(page.IsCheckVisible);
        Assert.True(page.HasCheckFindings);
        Assert.Equal(2, page.CheckFindings.Count);
        Assert.Contains("2", page.CheckTitle);

        page.CloseCheckCommand.Execute(null);
        Assert.False(page.IsCheckVisible);
    }

    [AvaloniaFact]
    public async Task TheCheckCard_Renders()
    {
        var page = Create(out var profile);
        await page.RunCheckAsync(profile);

        string path = Shot.Render(new ProfileCheckCardView { DataContext = page }, "profile_check_card", 640, 260);
        Assert.True(new FileInfo(path).Length > 2000);
    }
}

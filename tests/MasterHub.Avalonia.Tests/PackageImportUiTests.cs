using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Core.Actions;
using MasterHub.Core.Dialogs;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Packages;
using MasterHub.Core.Security;

namespace MasterHub.UiTests;

/// <summary>T-09 on the Profiles page: .mhdeck export, and an import that shows what can run a program before anything is saved.</summary>
public sealed class PackageImportUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh_pkg_ui_" + Guid.NewGuid().ToString("N"));

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

    private (ProfileService Profiles, ProfilesPageViewModel Page, ConfirmDialogService Confirm) Machine(string name)
    {
        string dir = Path.Combine(_root, name);
        var profiles = new ProfileService(dir, new Plain());
        var confirm = new ConfirmDialogService();
        var page = new ProfilesPageViewModel(profiles, confirm, packages: new ProfilePackageService(profiles, new MediaStore(dir)));
        return (profiles, page, confirm);
    }

    private static void Pump() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private string ExportRiskyPackage()
    {
        var a = Machine("a");
        var profile = a.Profiles.CreateProfile("Tools", setAsActive: false);
        profile.Keys = new List<KeyAssignmentModel> { new() { Index = 0, Action = DeckActionType.OpenApplication, ActionParameter = @"C:\Tools\x.exe" } };
        a.Profiles.SaveProfile(profile);
        a.Page.RefreshProfiles();

        string package = Path.Combine(_root, "tools.mhdeck");
        a.Page.ExportProfile(a.Page.Profiles.Single(p => p.Id == profile.Id), package);
        Assert.True(File.Exists(package));
        return package;
    }

    [AvaloniaFact]
    public void ImportWithPrograms_AsksFirst_AndCancelSavesNothing()
    {
        string package = ExportRiskyPackage();
        var b = Machine("b");
        int before = b.Profiles.GetAllProfiles().Count;

        var import = b.Page.ImportProfileAsync(package);
        Assert.NotNull(b.Confirm.Current);                       // the list of programs is shown
        Assert.Contains("x.exe", b.Confirm.Current!.Message);

        b.Confirm.Cancel();
        Pump();
        import.GetAwaiter().GetResult();

        Assert.Equal(before, b.Profiles.GetAllProfiles().Count);
        Assert.False(b.Page.StatusIsError);
    }

    [AvaloniaFact]
    public void ImportWithPrograms_AfterConfirming_IsSavedUnderANewId()
    {
        string package = ExportRiskyPackage();
        var b = Machine("b");

        var import = b.Page.ImportProfileAsync(package);
        b.Confirm.Accept();
        Pump();
        import.GetAwaiter().GetResult();

        Assert.Contains(b.Profiles.GetAllProfiles(), p => p.Name.StartsWith("Tools"));
        Assert.Equal(2, b.Profiles.GetAllProfiles().Count);
    }

    [AvaloniaFact]
    public void BrokenPackage_ShowsAnError_NotACrash()
    {
        string bad = Path.Combine(_root, "bad.mhdeck");
        Directory.CreateDirectory(_root);
        File.WriteAllText(bad, "not a zip");
        var b = Machine("b");

        b.Page.ImportProfileAsync(bad).GetAwaiter().GetResult();

        Assert.True(b.Page.StatusIsError);
        Assert.False(string.IsNullOrWhiteSpace(b.Page.StatusMessage));
    }
}

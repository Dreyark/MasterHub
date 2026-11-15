using MasterHub.Core.Actions;
using MasterHub.Core.Apps;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Draft;
using MasterHub.Core.Profiles.Templates;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

/// <summary>T-08: what the tour suggests and how a ready-made profile becomes a draft.</summary>
public sealed class TourTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_tour_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static InstalledApp Program(string exe) =>
        new("start:" + exe, exe, InstalledAppKind.StartMenu, @"C:\apps\" + exe + ".lnk", @"C:\apps\" + exe + ".exe");

    private static InstalledApp Game(int n) =>
        new("steam:" + n, "Game " + n, InstalledAppKind.Steam, "steam://rungameid/" + n, $@"C:\games\g{n}\game{n}.exe");

    private static TourContext Context(string[]? running = null, params InstalledApp[] installed) =>
        new(running ?? Array.Empty<string>(), installed);

    // ── the suggestion ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void NothingToGoOn_SuggestsTheEverydayDesk()
    {
        Assert.Equal(TourFocus.Desk, TourAdvisor.Suggest(TourContext.Empty));
        Assert.Equal(TourFocus.Desk, TourAdvisor.Suggest(Context(new[] { "notepad", "chrome" }, Program("Calculator"))));
    }

    [Theory]
    [InlineData("obs64")]
    [InlineData("OBS64")]
    [InlineData("Streamlabs Desktop")]
    public void AnOpenStreamingProgram_SuggestsStreaming(string process) =>
        Assert.Equal(TourFocus.Streaming, TourAdvisor.Suggest(Context(new[] { process })));

    [Theory]
    [InlineData("Teams")]
    [InlineData("ms-teams")]
    [InlineData("Zoom")]
    public void AnOpenCallProgram_SuggestsMeetings(string process) =>
        Assert.Equal(TourFocus.Meetings, TourAdvisor.Suggest(Context(new[] { process })));

    [Fact]
    public void ThreeInstalledGames_SuggestGaming_TwoDoNot()
    {
        Assert.Equal(TourFocus.Gaming, TourAdvisor.Suggest(Context(null, Game(1), Game(2), Game(3))));
        Assert.Equal(TourFocus.Desk, TourAdvisor.Suggest(Context(null, Game(1), Game(2))));
    }

    [Fact]
    public void InstalledButNotOpen_StillCounts_ButAfterWhatIsOpenAndAfterGames()
    {
        Assert.Equal(TourFocus.Streaming, TourAdvisor.Suggest(Context(null, Program("obs64"))));
        Assert.Equal(TourFocus.Meetings, TourAdvisor.Suggest(Context(null, Program("Discord"))));

        // an open call program beats an installed streaming one; installed games beat installed streaming / call programs
        Assert.Equal(TourFocus.Meetings, TourAdvisor.Suggest(Context(new[] { "Teams" }, Program("obs64"))));
        Assert.Equal(TourFocus.Gaming, TourAdvisor.Suggest(Context(null, Game(1), Game(2), Game(3), Program("obs64"))));
    }

    [Fact]
    public void WhenBothAreOpen_StreamingWins()
    {
        Assert.Equal(TourFocus.Streaming, TourAdvisor.Suggest(Context(new[] { "Teams", "obs64" })));
    }

    [Fact]
    public void Noticed_ListsTheRelevantProgramsOnce()
    {
        var noticed = TourAdvisor.Noticed(Context(new[] { "obs64", "chrome", "Discord" }, Program("Discord"), Program("Teams")));

        Assert.Equal(new[] { "obs64", "Discord", "Teams" }, noticed.ToArray());
    }

    // ── answers map to real templates ───────────────────────────────────────────────────────

    [Fact]
    public void EveryAnswer_MapsToAnEmbeddedTemplate_AndEveryTemplateIsReachable()
    {
        var ids = ProfileTemplateService.LoadEmbedded().Select(t => t.Id).ToHashSet();

        foreach (var focus in Enum.GetValues<TourFocus>())
            Assert.Contains(TourAdvisor.TemplateId(focus), ids);

        Assert.Equal(ids.Count, Enum.GetValues<TourFocus>().Select(TourAdvisor.TemplateId).Distinct().Count());
    }

    // ── building a draft from a template ────────────────────────────────────────────────────

    private ProfileService Profiles() => new(_dir, new SecretProtectionService());

    [Fact]
    public void Build_DoesNotSaveAnything_UnlikeInstantiate()
    {
        var profiles = Profiles();
        var service = new ProfileTemplateService(profiles);
        var template = service.GetAll().First(t => t.Id == "obs-streaming");
        int before = profiles.GetAllProfiles().Count;

        var built = service.Build(template);

        Assert.Equal(before, profiles.GetAllProfiles().Count);
        Assert.Contains(built.Keys, k => k.Action != DeckActionType.None);

        var saved = service.Instantiate(template);
        Assert.Equal(before + 1, profiles.GetAllProfiles().Count);
        Assert.Equal(built.Keys.Count(k => k.Action != DeckActionType.None), saved.Keys.Count(k => k.Action != DeckActionType.None));
    }

    [Fact]
    public void EveryTemplate_CanBeShownAsADraft_AndKept()
    {
        foreach (var template in ProfileTemplateService.LoadEmbedded())
        {
            var profiles = Profiles();
            var service = new ProfileTemplateService(profiles);
            int before = profiles.GetAllProfiles().Count;

            var draft = profiles.BeginDraft(service.Build(template), DraftMode.Preview);
            string draftId = draft.Id;
            Assert.Equal(before, profiles.GetAllProfiles().Count);                       // not saved among the profiles
            Assert.Equal(template.Keys.Count, profiles.GetActiveProfile().Keys.Count(k => k.Action != DeckActionType.None));

            var kept = profiles.KeepDraft();
            Assert.Equal(before + 1, profiles.GetAllProfiles().Count);
            Assert.NotEqual(draftId, kept.Id);

            Directory.Delete(_dir, true);                                                // a fresh data folder for the next template
        }
    }

    [Fact]
    public void DroppedKeys_AreEmptied_TheOthersStay()
    {
        var service = new ProfileTemplateService(Profiles());
        var template = service.GetAll().First(t => t.Id == "everyday-desk");
        var profile = service.Build(template);
        int first = template.Keys[0].Index;
        int second = template.Keys[1].Index;

        TourAdvisor.DropKeys(profile, new HashSet<int> { first });

        Assert.Equal(DeckActionType.None, profile.Keys.First(k => k.Index == first).Action);
        Assert.Equal("", profile.Keys.First(k => k.Index == first).CustomTitle);
        Assert.NotEqual(DeckActionType.None, profile.Keys.First(k => k.Index == second).Action);
        Assert.Equal(DeckActionType.None, profile.Pages[0].Keys.First(k => k.Index == first).Action);   // page 1 follows
    }

    [Fact]
    public void TheTourState_IsPartOfTheAppSettings_AndSurvivesARestart()
    {
        var profiles = Profiles();
        Assert.Equal(TourState.NotOffered, profiles.GetAppSettings().TourState);

        var settings = profiles.GetAppSettings();
        settings.TourState = TourState.Done;
        profiles.SaveAppSettings(settings);

        Assert.Equal(TourState.Done, Profiles().GetAppSettings().TourState);
    }
}

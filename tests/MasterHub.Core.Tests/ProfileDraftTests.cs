using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Draft;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

/// <summary>T-08: a draft is shown without being saved among the profiles; keep, discard, trial and the restore after a crash.</summary>
public sealed class ProfileDraftTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_draft_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private ProfileService Create() => new(_dir, new SecretProtectionService());

    private static ProfileModel Draft(string name = "Streaming", DeckActionType action = DeckActionType.ObsToggleStream)
    {
        var p = ProfileModel.CreateEmpty(name);
        p.Keys = new List<KeyAssignmentModel> { new() { Index = 0, Action = action, CustomTitle = "GO LIVE" } };
        p.Pages[0].Keys = new List<KeyAssignmentModel>(p.Keys);
        return p;
    }

    private string DraftFile => Path.Combine(_dir, "drafts", "draft.json");
    private string SettingsFile => Path.Combine(_dir, "settings.json");
    private int ProfileFiles => Directory.GetFiles(Path.Combine(_dir, "profiles"), "*.json").Length;

    /// <summary>The user's own profile with a recognisable key, so "nothing of it changed" can be checked.</summary>
    private static ProfileModel UserProfile(ProfileService profiles)
    {
        var mine = profiles.GetActiveProfile();
        mine.Keys = new List<KeyAssignmentModel> { new() { Index = 2, Action = DeckActionType.MicMute, CustomTitle = "MY MIC" } };
        mine.Pages[0].Keys = new List<KeyAssignmentModel>(mine.Keys);
        profiles.SaveProfile(mine);
        return profiles.GetActiveProfile();
    }

    // ── begin ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ADraft_IsTheActiveProfile_ButNotAmongTheSavedOnes()
    {
        var profiles = Create();
        var mine = UserProfile(profiles);
        int before = ProfileFiles;
        int changes = 0;
        profiles.DraftChanged += () => changes++;

        var draft = profiles.BeginDraft(Draft(), DraftMode.Preview);

        Assert.Equal(draft.Id, profiles.GetActiveProfile().Id);
        Assert.Equal("GO LIVE", profiles.GetActiveProfile().Keys[0].CustomTitle);
        Assert.DoesNotContain(profiles.GetAllProfiles(), p => p.Id == draft.Id);   // the Profiles page, the API and auto-switch never see it
        Assert.Equal(before, ProfileFiles);
        Assert.True(File.Exists(DraftFile));
        Assert.Equal(mine.Id, profiles.ActiveDraft!.OriginProfileId);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheUsersProfile_IsNotTouchedWhileADraftIsShown()
    {
        var profiles = Create();
        UserProfile(profiles);
        string profileJson = File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "profiles"), "*.json").Single());
        string settingsJson = File.ReadAllText(SettingsFile);

        var draft = profiles.BeginDraft(Draft(), DraftMode.Preview);
        draft.Keys[0].CustomTitle = "EDITED IN THE DRAFT";
        profiles.SaveProfile(draft);                               // what the Studio's auto-save does

        Assert.Equal(profileJson, File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "profiles"), "*.json").Single()));
        Assert.Equal(settingsJson, File.ReadAllText(SettingsFile));   // "the active profile" was not rewritten either
        Assert.Contains("EDITED IN THE DRAFT", File.ReadAllText(DraftFile));
    }

    [Fact]
    public void ADraft_NeverCarriesSecretsOrAutoSwitchRules()
    {
        var profiles = Create();
        var d = Draft();
        d.ObsPassword = "secret";
        d.TwitchOAuthToken = "oauth";
        d.SmartRules = new List<SmartProfileRule> { new() { ProcessName = "obs64" } };

        profiles.BeginDraft(d, DraftMode.Preview);

        Assert.DoesNotContain("secret", File.ReadAllText(DraftFile));
        Assert.DoesNotContain("oauth", File.ReadAllText(DraftFile));
        Assert.Empty(profiles.GetActiveProfile().SmartRules);
    }

    [Fact]
    public void ASecondDraft_ReplacesTheFirst_AndTheWayBackStaysTheOriginalProfile()
    {
        var profiles = Create();
        var mine = UserProfile(profiles);

        profiles.BeginDraft(Draft("One"), DraftMode.Preview);
        profiles.BeginDraft(Draft("Two"), DraftMode.Preview);

        Assert.Equal("Two", profiles.GetActiveProfile().Name);
        Assert.Equal(mine.Id, profiles.ActiveDraft!.OriginProfileId);   // not the first draft
        profiles.DiscardDraft();
        Assert.Equal(mine.Id, profiles.GetActiveProfile().Id);
    }

    // ── discard ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Discard_BringsTheOriginalProfileBack_AndLeavesNoTrace()
    {
        var profiles = Create();
        var mine = UserProfile(profiles);
        profiles.BeginDraft(Draft(), DraftMode.Trial, 20);
        int changes = 0;
        profiles.DraftChanged += () => changes++;

        profiles.DiscardDraft();

        Assert.Null(profiles.ActiveDraft);
        Assert.Equal(mine.Id, profiles.GetActiveProfile().Id);
        Assert.Equal("MY MIC", profiles.GetActiveProfile().Keys.Single().CustomTitle);
        Assert.False(File.Exists(DraftFile));
        Assert.Single(profiles.GetAllProfiles());
        Assert.Equal(1, changes);

        profiles.DiscardDraft();                                   // nothing to throw away: nothing reported
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ChoosingAProfile_LeavesThePreview()
    {
        var profiles = Create();
        var mine = UserProfile(profiles);
        var other = profiles.CreateProfile("Other", setAsActive: false);
        profiles.BeginDraft(Draft(), DraftMode.Preview);

        profiles.SetActiveProfile(other.Id);

        Assert.Null(profiles.ActiveDraft);
        Assert.Equal(other.Id, profiles.GetActiveProfile().Id);
        Assert.False(File.Exists(DraftFile));
        _ = mine;
    }

    [Fact]
    public void AutoSwitching_AlsoLeavesThePreview_SoTheUiMustNotAutoSwitchDuringOne()
    {
        // The service itself cannot tell an automatic switch from a manual one; the Profiles page skips auto-switching while
        // ActiveDraft is set. This test records that a switch ends the draft, which is why that check matters.
        var profiles = Create();
        var other = profiles.CreateProfile("Other", setAsActive: false);
        profiles.BeginDraft(Draft(), DraftMode.Preview);

        profiles.SetActiveProfile(other.Id, persist: false);

        Assert.Null(profiles.ActiveDraft);
    }

    // ── keep ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Keep_MakesItARealProfile_WithANewId_AndTheActiveOne()
    {
        var profiles = Create();
        UserProfile(profiles);
        var draft = profiles.BeginDraft(Draft("Streaming"), DraftMode.Preview);
        string draftId = draft.Id;

        var kept = profiles.KeepDraft("My stream deck");

        Assert.NotEqual(draftId, kept.Id);
        Assert.Equal("My stream deck", kept.Name);
        Assert.False(kept.IsDefault);
        Assert.Null(profiles.ActiveDraft);
        Assert.False(File.Exists(DraftFile));
        Assert.Contains(profiles.GetAllProfiles(), p => p.Id == kept.Id && p.Keys.Any(k => k.CustomTitle == "GO LIVE"));
        Assert.Equal(kept.Id, profiles.GetActiveProfile().Id);

        // after a restart the kept profile is still the active one (it was a manual choice)
        Assert.Equal(kept.Id, Create().GetActiveProfile().Id);
    }

    [Fact]
    public void Keep_KeepsTheDraftsOwnNameWhenNoneIsGiven()
    {
        var profiles = Create();
        profiles.BeginDraft(Draft("Game bar"), DraftMode.Preview);

        Assert.Equal("Game bar", profiles.KeepDraft().Name);
    }

    [Fact]
    public void Keep_WithoutADraft_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => Create().KeepDraft());
    }

    [Fact]
    public void AfterKeeping_EditsGoToTheRealProfile_NotToADraftFile()
    {
        var profiles = Create();
        profiles.BeginDraft(Draft(), DraftMode.Preview);
        var kept = profiles.KeepDraft();

        kept.Keys[0].CustomTitle = "AFTER KEEP";
        profiles.SaveProfile(kept);

        Assert.False(File.Exists(DraftFile));
        Assert.Contains("AFTER KEEP", File.ReadAllText(Path.Combine(_dir, "profiles", kept.Id + ".json")));
    }

    // ── crash restore ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void ADraftLeftByACrash_IsOfferedAtTheNextStart_AndTheUsersProfileIsStillActive()
    {
        var first = Create();
        var mine = UserProfile(first);
        var draft = first.BeginDraft(Draft("Streaming"), DraftMode.Trial, 20);
        draft.Keys[0].CustomTitle = "EDITED";
        first.SaveProfile(draft);
        // the process dies here: no Discard, no Keep

        var second = Create();

        Assert.NotNull(second.PendingDraft);
        Assert.Equal("Streaming", second.PendingDraft!.Name);
        Assert.Null(second.ActiveDraft);                           // nothing is on the deck yet
        Assert.Equal(mine.Id, second.GetActiveProfile().Id);        // the deck comes back as the user left it
        Assert.Equal("MY MIC", second.GetActiveProfile().Keys.Single().CustomTitle);
    }

    [Fact]
    public void PuttingADraftBack_ContinuesWhereItStopped_AsAPreviewWithoutAClock()
    {
        var first = Create();
        UserProfile(first);
        var draft = first.BeginDraft(Draft(), DraftMode.Trial, 20);
        draft.Keys[0].CustomTitle = "EDITED";
        first.SaveProfile(draft);

        var second = Create();
        var back = second.ResumePendingDraft();

        Assert.NotNull(back);
        Assert.Equal("EDITED", second.GetActiveProfile().Keys[0].CustomTitle);
        Assert.Equal(DraftMode.Preview, second.ActiveDraft!.Mode);   // a trial that survived a crash does not run out on its own
        Assert.Null(second.PendingDraft);
    }

    [Fact]
    public void NotNow_ForgetsTheLeftDraft()
    {
        var first = Create();
        first.BeginDraft(Draft(), DraftMode.Preview);

        var second = Create();
        second.DropPendingDraft();

        Assert.Null(second.PendingDraft);
        Assert.False(File.Exists(DraftFile));
        Assert.Null(Create().PendingDraft);
    }

    [Fact]
    public void ACorruptDraftFile_IsSetAside_NotDeleted_NotCrashing()
    {
        Create();
        Directory.CreateDirectory(Path.GetDirectoryName(DraftFile)!);
        File.WriteAllText(DraftFile, "{ this is not json");

        var profiles = Create();

        Assert.Null(profiles.PendingDraft);
        Assert.False(File.Exists(DraftFile));
        Assert.Contains(Directory.GetFiles(Path.GetDirectoryName(DraftFile)!), f => f.Contains(".corrupt-"));
        Assert.NotEmpty(profiles.QuarantinedFiles);
    }

    [Fact]
    public void ResumeWithNothingLeft_ReturnsNull()
    {
        Assert.Null(Create().ResumePendingDraft());
    }

    // ── trial clock ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ATrial_CountsDownFromItsStart_AndAPreviewNeverExpires()
    {
        var start = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var trial = new DraftInfo("d", "n", "o", DraftMode.Trial, start, 20);
        var preview = new DraftInfo("d", "n", "o", DraftMode.Preview, start, 0);

        Assert.Equal(20, trial.SecondsLeft(start));
        Assert.Equal(11, trial.SecondsLeft(start.AddSeconds(9.5)));
        Assert.False(trial.IsExpired(start.AddSeconds(19)));
        Assert.True(trial.IsExpired(start.AddSeconds(20)));
        Assert.Equal(0, trial.SecondsLeft(start.AddMinutes(5)));

        Assert.False(preview.IsExpired(start.AddDays(1)));
        Assert.Equal(0, preview.SecondsLeft(start));
    }

    [Fact]
    public void ATrial_HasAtLeastFiveSeconds()
    {
        var profiles = Create();
        profiles.BeginDraft(Draft(), DraftMode.Trial, 1);

        Assert.Equal(5, profiles.ActiveDraft!.TrialSeconds);
    }
}

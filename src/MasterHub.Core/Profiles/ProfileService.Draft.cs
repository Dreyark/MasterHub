using System;
using System.IO;
using System.Text.Json;
using MasterHub.Core.Profiles.Draft;

namespace MasterHub.Core.Profiles;

/// <summary>Drafts (T-08): a profile shown on the deck without being saved among the user's profiles.</summary>
public partial class ProfileService
{
    private ProfileModel? _activeDraft;
    private DraftInfo? _activeDraftInfo;
    private string _draftFilePath = "";

    public event Action? DraftChanged;

    public DraftInfo? ActiveDraft
    {
        get { lock (_fileLock) return _activeDraftInfo; }
    }

    /// <summary>A draft the last run left behind (a crash, a power cut). Offered at start: put it back, keep it or let it go.</summary>
    public DraftInfo? PendingDraft { get; private set; }

    private void InitializeDrafts(string baseDir)
    {
        _draftFilePath = Path.Combine(baseDir, "drafts", "draft.json");

        try
        {
            if (!File.Exists(_draftFilePath)) return;

            var envelope = JsonSerializer.Deserialize<DraftEnvelope>(File.ReadAllText(_draftFilePath), _jsonOptions);
            if (envelope?.Info == null || envelope.Profile == null) throw new JsonException("Not a draft.");
            PendingDraft = envelope.Info;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Quarantine(_draftFilePath);   // an unreadable draft is set aside like any other unreadable file, never deleted
        }
        catch
        {
            // the file is locked by someone else: leave it alone, the next start looks again
        }
    }

    /// <summary>
    /// Shows <paramref name="draft"/> instead of the active profile without saving it as a profile. A draft started while another
    /// one is active replaces it (the profile to come back to stays the original one). Credentials are cleared: a draft is meant
    /// for templates and the tour, and nothing secret may be written to the draft file.
    /// </summary>
    public ProfileModel BeginDraft(ProfileModel draft, DraftMode mode, int trialSeconds = 0)
    {
        if (draft == null) throw new ArgumentNullException(nameof(draft));

        lock (_fileLock)
        {
            string origin = _activeDraftInfo?.OriginProfileId ?? GetActiveProfile().Id;

            draft.Id = Guid.NewGuid().ToString("N");
            draft.IsDefault = false;
            draft.SmartRules = new List<SmartProfileRule>();   // a draft never takes part in auto-switching
            draft.ObsPassword = null;
            draft.TwitchOAuthToken = null;
            draft.YouTubeApiKey = null;
            draft.StreamlabsApiToken = null;
            draft.DiscordAccessToken = null;

            _activeDraft = draft;
            _activeDraftInfo = new DraftInfo(draft.Id, draft.Name, origin, mode, DateTime.UtcNow, mode == DraftMode.Trial ? Math.Max(5, trialSeconds) : 0);
            _transientActiveProfileId = draft.Id;
            PendingDraft = null;
            WriteDraftFile();
        }

        DraftChanged?.Invoke();
        return draft;
    }

    /// <summary>Makes the draft a real profile (with a new id), the active one. Returns it.</summary>
    public ProfileModel KeepDraft(string? name = null)
    {
        ProfileModel kept;
        lock (_fileLock)
        {
            kept = _activeDraft ?? throw new InvalidOperationException("There is no draft to keep.");
            ClearDraftState();

            kept.Id = Guid.NewGuid().ToString("N");
            if (!string.IsNullOrWhiteSpace(name)) kept.Name = name.Trim();
            SaveProfile(kept);
            SetActiveProfile(kept.Id, persist: true);
        }

        DraftChanged?.Invoke();
        return kept;
    }

    /// <summary>Throws the draft away; the profile that was active before is active again. Nothing of it is kept.</summary>
    public void DiscardDraft()
    {
        lock (_fileLock)
        {
            if (_activeDraft == null) return;
            ClearDraftState();
        }

        DraftChanged?.Invoke();
    }

    /// <summary>Puts a draft left by an earlier run back on the deck (as a preview that stays until the user decides).</summary>
    public ProfileModel? ResumePendingDraft()
    {
        ProfileModel? profile;
        lock (_fileLock)
        {
            if (PendingDraft == null || !File.Exists(_draftFilePath)) return null;

            DraftEnvelope? envelope;
            try { envelope = JsonSerializer.Deserialize<DraftEnvelope>(File.ReadAllText(_draftFilePath), _jsonOptions); }
            catch { envelope = null; }

            if (envelope?.Info == null || envelope.Profile == null)
            {
                PendingDraft = null;
                return null;
            }

            profile = envelope.Profile;
            _activeDraft = profile;
            _activeDraftInfo = envelope.Info with { Mode = DraftMode.Preview, TrialSeconds = 0, StartedUtc = DateTime.UtcNow };
            _transientActiveProfileId = profile.Id;
            PendingDraft = null;
            WriteDraftFile();
        }

        DraftChanged?.Invoke();
        return profile;
    }

    /// <summary>"Not now": forgets a draft left by an earlier run.</summary>
    public void DropPendingDraft()
    {
        lock (_fileLock)
        {
            if (PendingDraft == null) return;
            PendingDraft = null;
            try { if (File.Exists(_draftFilePath)) File.Delete(_draftFilePath); } catch { }
        }

        DraftChanged?.Invoke();
    }

    // ── internals ───────────────────────────────────────────────────────────────────────────

    /// <summary>Called with <c>_fileLock</c> held.</summary>
    private void ClearDraftState()
    {
        if (_activeDraft != null && _transientActiveProfileId == _activeDraft.Id) _transientActiveProfileId = null;
        _activeDraft = null;
        _activeDraftInfo = null;
        try { if (File.Exists(_draftFilePath)) File.Delete(_draftFilePath); } catch { }
    }

    /// <summary>Called with <c>_fileLock</c> held.</summary>
    private void WriteDraftFile()
    {
        if (_activeDraft == null || _activeDraftInfo == null) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_draftFilePath)!);
            AtomicWriteText(_draftFilePath, JsonSerializer.Serialize(new DraftEnvelope { Info = _activeDraftInfo, Profile = _activeDraft }, _jsonOptions));
        }
        catch
        {
            // a draft that cannot be written still works for this run; it just will not survive a crash
        }
    }

    /// <summary>Whether <paramref name="profile"/> is the draft on the deck (called with <c>_fileLock</c> held).</summary>
    private bool TrySaveAsDraft(ProfileModel profile)
    {
        if (_activeDraft == null || profile.Id != _activeDraft.Id) return false;

        _activeDraft = profile;
        WriteDraftFile();
        return true;
    }
}

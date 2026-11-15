using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Draft;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// The draft that is on the deck (T-08): the banner with <i>Keep</i> / <i>Throw away</i>, the trial countdown, the notes of what the
/// dry-run blocked, and the offer to put back a draft that a crash left behind. A draft never touches the user's own profile: it
/// is shown on the deck and in the window, and only becomes a profile when the user keeps it.
/// </summary>
public sealed partial class DraftSessionViewModel : ObservableObject, IDisposable
{
    private readonly IProfileService _profiles;
    private readonly CanvasViewModel _canvas;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _blockedAtUtc;
    private Func<DateTime> _now = () => DateTime.UtcNow;

    public DraftSessionViewModel(IProfileService profiles, CanvasViewModel canvas)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

        _profiles.DraftChanged += OnDraftChanged;
        _canvas.DryRunBlocked += OnBlocked;
        _clock.Tick += (_, _) => OnClock();
    }

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _countdownText = "";
    [ObservableProperty] private bool _hasCountdown;
    [ObservableProperty] private string _blockedText = "";
    [ObservableProperty] private bool _hasBlocked;

    /// <summary>True for the offer of a draft that an earlier run left (put it back / use this / not now).</summary>
    [ObservableProperty] private bool _isPendingOffer;

    /// <summary>True while a draft is on the deck (the tour and the Profiles page use this).</summary>
    public bool IsActive => _profiles.ActiveDraft != null;

    /// <summary>Raised after the draft was kept, with the new profile.</summary>
    public event Action<ProfileModel>? Kept;

    /// <summary>Raised when the draft is gone (kept or thrown away), whoever ended it.</summary>
    public event Action? Ended;

    /// <summary>Tests set a clock; the app uses the real one.</summary>
    internal void UseClock(Func<DateTime> now) => _now = now;

    // ── starting ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows <paramref name="draft"/> on the deck. The profile that is on the deck now is saved first, so nothing of it is lost,
    /// and the controls run nothing real until the draft is kept or thrown away.
    /// </summary>
    public void Start(ProfileModel draft, DraftMode mode, int trialSeconds = 20)
    {
        // an unsaved edit of the user's profile goes into that profile; once the draft is active the canvas never writes into it
        if (!IsActive) _canvas.SaveCurrentStateToActiveProfile();

        _profiles.BeginDraft(draft, mode, trialSeconds);
        _canvas.IsDryRun = true;
        _canvas.ApplyProfile(_profiles.GetActiveProfile(), saveCurrent: false);
        Refresh();
    }

    // ── the user decides ────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void KeepIt() => Keep();

    /// <summary>Makes the draft a real profile and the active one. Returns it (<c>null</c> when no draft is on the deck).</summary>
    public ProfileModel? Keep()
    {
        if (!IsActive) return null;

        var kept = _profiles.KeepDraft();
        _canvas.IsDryRun = false;
        _canvas.ApplyProfile(kept, saveCurrent: false);
        Refresh();

        Kept?.Invoke(kept);
        return kept;
    }

    [RelayCommand]
    public void Discard()
    {
        if (!IsActive) return;

        _profiles.DiscardDraft();
        _canvas.IsDryRun = false;
        _canvas.ApplyProfile(_profiles.GetActiveProfile(), saveCurrent: false);   // the user's own profile is back on the deck
        Refresh();
    }

    // ── a draft left by an earlier run ──────────────────────────────────────────────────────

    /// <summary>Offers a draft the last run left behind (call once at start).</summary>
    public void OfferPending()
    {
        var pending = _profiles.PendingDraft;
        if (pending == null) return;

        IsPendingOffer = true;
        Title = string.Format(LocalizationService.Instance.GetString("Draft_PendingTitle", "A draft from the last run was left: \"{0}\""), pending.Name);
        HasCountdown = false;
        IsVisible = true;
    }

    [RelayCommand]
    private void PutBack()
    {
        var profile = _profiles.ResumePendingDraft();
        IsPendingOffer = false;
        if (profile == null) { IsVisible = false; return; }

        _canvas.SaveCurrentStateToActiveProfile();
        _canvas.IsDryRun = true;
        _canvas.ApplyProfile(_profiles.GetActiveProfile(), saveCurrent: false);
        Refresh();
    }

    [RelayCommand]
    private void UseThis()
    {
        var profile = _profiles.ResumePendingDraft();
        IsPendingOffer = false;
        if (profile == null) { IsVisible = false; return; }

        Keep();
    }

    [RelayCommand]
    private void NotNow()
    {
        _profiles.DropPendingDraft();
        IsPendingOffer = false;
        IsVisible = false;
    }

    // ── state ───────────────────────────────────────────────────────────────────────────────

    private void OnDraftChanged()
    {
        // can come from any thread (and from a profile switch that ended the draft)
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        var info = _profiles.ActiveDraft;
        bool wasDryRun = _canvas.IsDryRun;

        if (info == null)
        {
            _clock.Stop();
            _canvas.IsDryRun = false;
            if (!IsPendingOffer) IsVisible = false;
            HasCountdown = false;
            HasBlocked = false;
            OnPropertyChanged(nameof(IsActive));
            if (wasDryRun) Ended?.Invoke();
            return;
        }

        _canvas.IsDryRun = true;
        IsPendingOffer = false;
        IsVisible = true;
        Title = string.Format(LocalizationService.Instance.GetString("Draft_Title", "Preview: {0}"), info.Name);
        UpdateCountdown(info);
        OnPropertyChanged(nameof(IsActive));

        if (!_clock.IsEnabled) _clock.Start();
    }

    private void UpdateCountdown(DraftInfo info)
    {
        HasCountdown = info.IsTrial;
        CountdownText = info.IsTrial
            ? string.Format(LocalizationService.Instance.GetString("Draft_SecondsLeft", "{0} s left"), info.SecondsLeft(_now()))
            : "";
    }

    /// <summary>One tick a second while a draft is on the deck: the trial countdown and the fading of the blocked-press note.</summary>
    internal void OnClock()
    {
        var info = _profiles.ActiveDraft;
        if (info == null) { _clock.Stop(); return; }

        if (info.IsExpired(_now()))
        {
            Discard();   // a trial that was not kept puts itself away
            return;
        }

        UpdateCountdown(info);
        if (HasBlocked && (_now() - _blockedAtUtc).TotalSeconds >= 5) HasBlocked = false;
    }

    private void OnBlocked(DryRunNotice notice)
    {
        void Show()
        {
            var loc = LocalizationService.Instance;
            BlockedText = notice.KeyNumber > 0
                ? string.Format(loc.GetString("Draft_BlockedKey", "Preview: key {0} would run \"{1}\" – nothing was run."), notice.KeyNumber, notice.ActionName)
                : string.Format(loc.GetString("Draft_BlockedControl", "Preview: \"{0}\" was not run."), notice.ActionName);
            _blockedAtUtc = _now();
            HasBlocked = true;
        }

        if (Dispatcher.UIThread.CheckAccess()) Show();
        else Dispatcher.UIThread.Post(Show);
    }

    public void Dispose()
    {
        _clock.Stop();
        _profiles.DraftChanged -= OnDraftChanged;
        _canvas.DryRunBlocked -= OnBlocked;
    }
}

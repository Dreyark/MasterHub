using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Apps;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Auto;
using MasterHub.Core.Profiles.Draft;
using MasterHub.Core.Profiles.Templates;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// The assistant (T-37): profiles that prepare themselves for the programs on this PC, small suggestions, and the (optional)
/// counting that feeds them. <b>Everything is off until the user says yes</b>, one switch turns it all off, and the numbers
/// it counts stay in files on this PC. It never changes a profile by itself: a suggestion only opens a draft to look at.
/// </summary>
public sealed partial class AssistViewModel : ObservableObject, IDisposable
{
    /// <summary>How often the suggestions are looked at again while the app runs.</summary>
    private static readonly TimeSpan EvaluateEvery = TimeSpan.FromMinutes(3);

    private readonly IAssistSettingsStore _store;
    private readonly IAppUsageService _usage;
    private readonly IPressCounter _presses;
    private readonly IAutoProfileService _auto;
    private readonly INudgeService _nudges;
    private readonly IProfileService _profiles;
    private readonly IInstalledAppsService? _installed;
    private readonly IMasterHubDeviceService? _device;
    private readonly IProfileTemplateService? _templates;
    private readonly DraftSessionViewModel _draft;
    private readonly TourViewModel _tour;
    private readonly DispatcherTimer _timer = new() { Interval = EvaluateEvery };
    private bool _loading;
    private string? _pendingLink;

    public AssistViewModel(IAssistSettingsStore store, IAppUsageService usage, IPressCounter presses, IAutoProfileService auto,
        INudgeService nudges, IProfileService profiles, DraftSessionViewModel draft, TourViewModel tour,
        IInstalledAppsService? installed = null, IMasterHubDeviceService? device = null, IProfileTemplateService? templates = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _presses = presses ?? throw new ArgumentNullException(nameof(presses));
        _auto = auto ?? throw new ArgumentNullException(nameof(auto));
        _nudges = nudges ?? throw new ArgumentNullException(nameof(nudges));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _tour = tour ?? throw new ArgumentNullException(nameof(tour));
        _installed = installed;
        _device = device;
        _templates = templates;

        var settings = _store.Read();
        _loading = true;
        IsEnabled = settings.Enabled;
        PressCounting = settings.PressCounting;
        _loading = false;

        _usage.AppInFront += OnAppInFront;
        _auto.Changed += () => Post(OnAutoProfilesChanged);
        _nudges.Changed += () => Post(RefreshCards);
        _draft.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(DraftSessionViewModel.IsVisible) or nameof(DraftSessionViewModel.IsActive)) RefreshCards(); };
        _tour.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TourViewModel.IsOpen)) RefreshCards(); };
        _draft.Kept += OnDraftKept;
        _draft.Ended += () => Dispatcher.UIThread.Post(() => _pendingLink = null);   // queued: Kept runs synchronously right behind Ended, and needs the link
        if (_device != null) _device.DevicesUpdated += () => Post(ModulesChanged);
        _timer.Tick += (_, _) => { if (IsEnabled) _nudges.Evaluate(); };

        RefreshData();
    }

    // ── state the views bind to ─────────────────────────────────────────────────────────────

    /// <summary>The master switch. Turning it on starts the looking; turning it off stops all of it (the numbers stay until they are deleted).</summary>
    [ObservableProperty] private bool _isEnabled;

    /// <summary>Counting of key presses (needs <see cref="IsEnabled"/>); off by default.</summary>
    [ObservableProperty] private bool _pressCounting;

    /// <summary>The one-time question "prepare profiles for your programs?" is on screen.</summary>
    [ObservableProperty] private bool _showOffer;

    [ObservableProperty] private bool _hasNudge;
    [ObservableProperty] private string _nudgeText = "";
    [ObservableProperty] private string _nudgeAccept = "";
    [ObservableProperty] private string _nudgeDismiss = "";

    /// <summary>How many prepared profiles the user has not looked at yet (the badge on "Profiles").</summary>
    [ObservableProperty] private int _unseenCount;

    /// <summary>What has been counted so far, in words (Settings).</summary>
    [ObservableProperty] private string _dataNote = "";

    public bool HasUnseen => UnseenCount > 0;

    partial void OnUnseenCountChanged(int value) => OnPropertyChanged(nameof(HasUnseen));

    /// <summary>The Profiles page should read its list again (a profile was prepared, switched on or linked).</summary>
    public event Action? ProfilesChanged;

    /// <summary>"Open the Studio" (a suggestion about a key points there). Set by the main view model.</summary>
    public Action? OpenStudioRequested { get; set; }

    // ── start ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Call once when the window is up: starts the looking when the user agreed earlier, and shows the question when not yet asked.</summary>
    public void Start()
    {
        if (IsEnabled) BeginAssisting(baseline: false);
        RefreshCards();
    }

    private void BeginAssisting(bool baseline)
    {
        _usage.Start();
        _timer.Start();

        // the modules that are connected when the user agrees were there before the assistant: they are "known", not new
        if (baseline) _store.Update(s => s.SeenModules ??= ConnectedKinds().ToList());
        _nudges.ModulesConnected(ConnectedKinds());

        _ = PrepareAsync();
    }

    private async Task PrepareAsync()
    {
        try
        {
            if (_installed != null && _installed.Apps.Count == 0) await _installed.RefreshAsync();
            await Task.Run(() => _auto.PrepareForInstalledApps());
        }
        catch
        {
            // a failed look at the installed programs only means nothing is prepared this time
        }
    }

    // ── the master switch and press counting ────────────────────────────────────────────────

    partial void OnIsEnabledChanged(bool value)
    {
        if (_loading) return;

        _store.Update(s =>
        {
            s.Enabled = value;
            s.Asked = true;
            if (!value) s.PressCounting = false;   // counting presses needs the switch
        });

        if (value)
        {
            BeginAssisting(baseline: true);
        }
        else
        {
            _usage.Stop();
            _presses.Flush();
            _timer.Stop();
            _loading = true;
            PressCounting = false;
            _loading = false;
            _nudges.Evaluate();
        }

        RefreshCards();
        RefreshData();
    }

    partial void OnPressCountingChanged(bool value)
    {
        if (_loading) return;
        _store.Update(s => s.PressCounting = value && s.Enabled);
        if (!value) _presses.Flush();
        RefreshData();
        _nudges.Evaluate();
    }

    [RelayCommand]
    private void AcceptOffer() => IsEnabled = true;

    [RelayCommand]
    private void DeclineOffer()
    {
        _store.Update(s => { s.Asked = true; s.Enabled = false; });
        RefreshCards();
    }

    /// <summary>Settings: "Delete counted data" – the time-in-front numbers and the press counts are forgotten.</summary>
    [RelayCommand]
    private void DeleteData()
    {
        _usage.Clear();
        _presses.Clear();
        _nudges.Evaluate();
        RefreshData();
    }

    // ── a suggestion ────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AcceptNudge()
    {
        var nudge = _nudges.Current;
        if (nudge == null) return;

        switch (nudge.Kind)
        {
            case NudgeKind.AppWithoutProfile:
            {
                var sketch = _auto.BuildSketch(nudge.Subject, nudge.Display);
                if (sketch != null)
                {
                    _pendingLink = sketch.Process;
                    _draft.Start(sketch.Profile, DraftMode.Preview);
                }
                break;
            }
            case NudgeKind.NewModule:
            {
                var sketch = ModuleSketch.Build(nudge.Subject, _profiles.GetActiveProfile(), _templates, Language());
                if (sketch != null)
                {
                    sketch.Name = string.Format(LocalizationService.Instance.GetString("Assist_SketchName", "Sketch: {0}"), ModuleName(nudge.Subject));
                    _draft.Start(sketch, DraftMode.Preview);
                }
                break;
            }
            case NudgeKind.UnusedKey:
                OpenStudioRequested?.Invoke();
                break;
        }

        _nudges.Answer(nudge);
    }

    [RelayCommand]
    private void DismissNudge()
    {
        var nudge = _nudges.Current;
        if (nudge != null) _nudges.Answer(nudge);
    }

    /// <summary>A sketch for a program was kept: it is linked to that program (switched on, the user kept it on purpose).</summary>
    private void OnDraftKept(ProfileModel kept)
    {
        string? process = _pendingLink;
        if (process == null) return;
        _pendingLink = null;

        kept.SmartRules.Add(new SmartProfileRule { ProfileId = kept.Id, ProcessName = process });
        _profiles.SaveProfile(kept);
        ProfilesChanged?.Invoke();
    }

    // ── prepared profiles ───────────────────────────────────────────────────────────────────

    private void OnAppInFront(string process)
    {
        // on the thread of the usage timer: the preparing touches files, so it never runs on the UI thread
        try { _auto.OnAppInFront(process); }
        catch { /* a program that cannot be looked up is simply not offered */ }
    }

    private void OnAutoProfilesChanged()
    {
        RefreshData();
        ProfilesChanged?.Invoke();
    }

    /// <summary>The user has been on the Profiles page: the "new" marks are done with.</summary>
    public void MarkProfilesSeen()
    {
        if (UnseenCount == 0) return;
        _auto.MarkSeen();
        RefreshData();
    }

    private void ModulesChanged()
    {
        if (IsEnabled) _nudges.ModulesConnected(ConnectedKinds());
    }

    private IEnumerable<string> ConnectedKinds()
    {
        if (_device == null) yield break;
        if (_device.IsKeypadConnected) yield return ModuleKinds.Keys;
        if (_device.IsKnobConnected) yield return ModuleKinds.Knobs;
        if (_device.IsSliderConnected) yield return ModuleKinds.Faders;
        if (_device.IsRollerConnected) yield return ModuleKinds.Rollers;
        if (_device.IsDialConnected) yield return ModuleKinds.Dial;
        if (_device.IsBaseConnected) yield return ModuleKinds.Base;
    }

    // ── what is shown ───────────────────────────────────────────────────────────────────────

    internal void RefreshCards()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshCards); return; }

        var settings = _store.Read();
        bool busy = _tour.IsOpen || _draft.IsVisible || _draft.IsActive;

        // the question waits until the tour and any draft are out of the way, and is asked only once
        ShowOffer = !settings.Asked && !busy;

        var nudge = _nudges.Current;
        if (nudge == null || busy)
        {
            HasNudge = false;
            return;
        }

        var loc = LocalizationService.Instance;
        switch (nudge.Kind)
        {
            case NudgeKind.AppWithoutProfile:
                NudgeText = string.Format(loc.GetString("Assist_NudgeApp", "You use {0} a lot, but it has no profile yet. Want to look at a sketch for it?"), nudge.Display);
                NudgeAccept = loc.GetString("Assist_ShowSketch", "Show a sketch");
                NudgeDismiss = loc.GetString("Assist_NoThanks", "No thanks");
                break;
            case NudgeKind.NewModule:
                NudgeText = string.Format(loc.GetString("Assist_NudgeModule", "A new part was connected for the first time: {0}. Want to look at a sketch for it?"), ModuleName(nudge.Subject));
                NudgeAccept = loc.GetString("Assist_ShowSketch", "Show a sketch");
                NudgeDismiss = loc.GetString("Assist_NoThanks", "No thanks");
                break;
            default:
            {
                string[] parts = nudge.Subject.Split('|');
                int number = parts.Length > 1 && int.TryParse(parts[1], out int index) ? index + 1 : 0;
                NudgeText = string.Format(loc.GetString("Assist_NudgeUnused", "Key {0} (\"{1}\") has not been pressed in a long time. Want to change it?"), number, nudge.Display);
                NudgeAccept = loc.GetString("Assist_OpenStudio", "Open the Studio");
                NudgeDismiss = loc.GetString("Assist_LeaveIt", "Leave it");
                break;
            }
        }

        HasNudge = true;
    }

    private void RefreshData()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshData); return; }

        UnseenCount = IsEnabled ? _auto.UnseenProfileIds().Count : 0;

        var loc = LocalizationService.Instance;
        var apps = _usage.Snapshot();
        long presses = _presses.IsActive || _presses.Since != null ? _presses.Get(_profiles.GetActiveProfile().Id).Values.Sum() : 0;
        DataNote = apps.Count == 0 && presses == 0
            ? loc.GetString("Assist_DataNone", "Nothing has been counted yet.")
            : string.Format(loc.GetString("Assist_DataNote", "Counted since {0}: {1} programs."), _usage.Since.ToString("d"), apps.Count)
              + (PressCounting ? " " + string.Format(loc.GetString("Assist_DataPresses", "{0} key presses on this profile."), presses) : "");
    }

    private string ModuleName(string kind)
    {
        var loc = LocalizationService.Instance;
        return kind switch
        {
            ModuleKinds.Keys => loc.GetString("Tour_ModKeypad", "keypad"),
            ModuleKinds.Dial => loc.GetString("Tour_ModDial", "dial"),
            ModuleKinds.Knobs => loc.GetString("Tour_ModKnobs", "knobs"),
            ModuleKinds.Faders => loc.GetString("Tour_ModFaders", "faders"),
            ModuleKinds.Rollers => loc.GetString("Tour_ModRollers", "rollers"),
            ModuleKinds.Base => loc.GetString("Tour_ModBase", "base"),
            _ => kind
        };
    }

    private string Language() => LocalizationService.Instance.CurrentLanguageCode?.Split('-')[0] ?? "en";

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        _timer.Stop();
        _usage.AppInFront -= OnAppInFront;
        _draft.Kept -= OnDraftKept;
    }
}

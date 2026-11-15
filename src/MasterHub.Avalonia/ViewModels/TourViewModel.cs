using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Apps;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Draft;
using MasterHub.Core.Profiles.Templates;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>One answer of the tour's question.</summary>
public sealed partial class TourFocusOption : ObservableObject
{
    public TourFocusOption(TourFocus focus, string name, string description)
    {
        Focus = focus;
        Name = name;
        Description = description;
    }

    public TourFocus Focus { get; }
    public string Name { get; }
    public string Description { get; }

    /// <summary>The answer the tour suggests from what it sees.</summary>
    [ObservableProperty] private bool _isSuggested;
}

/// <summary>One key of the draft that can be kept or dropped.</summary>
public sealed partial class TourPartViewModel : ObservableObject
{
    private readonly Action<TourPartViewModel> _changed;

    public TourPartViewModel(int index, string title, string action, Action<TourPartViewModel> changed)
    {
        KeyIndex = index;
        Title = string.IsNullOrWhiteSpace(title) ? action : title;
        Action = action;
        _changed = changed;
    }

    public int KeyIndex { get; }
    public string Title { get; }
    public string Action { get; }
    public string KeyText => $"{KeyIndex + 1}";

    [ObservableProperty] private bool _included = true;

    partial void OnIncludedChanged(bool value) => _changed(this);
}

/// <summary>
/// The guided setup (T-08): <i>show, then ask, then build</i>. One question about the main use, a look at what the app sees
/// (modules, programs), a draft built from a ready-made profile and shown on the deck, then each key can be kept or dropped.
/// Nothing is saved until <i>Keep</i>; until then the controls run nothing (dry-run) and the user's own profile is untouched.
/// Skippable, and it can be run again from Settings.
/// </summary>
public sealed partial class TourViewModel : ObservableObject
{
    public const int WelcomeStep = 0, FocusStep = 1, LookingStep = 2, ComposeStep = 3;

    private readonly DraftSessionViewModel _draft;
    private readonly IProfileService _profiles;
    private readonly IProfileTemplateService? _templates;
    private readonly IInstalledAppsService? _installedApps;
    private readonly IMasterHubDeviceService? _device;
    private readonly Func<IReadOnlyList<string>> _runningProcesses;
    private readonly HashSet<int> _dropped = new();
    private ProfileTemplate? _template;
    private TourContext _context = TourContext.Empty;
    private bool _building;

    /// <summary>True while the tour itself ends the draft (back, cancel, keep): that must not close the tour a second time.</summary>
    private bool _ending;

    public TourViewModel(DraftSessionViewModel draft, IProfileService profiles, IProfileTemplateService? templates,
        IInstalledAppsService? installedApps = null, IMasterHubDeviceService? device = null,
        Func<IReadOnlyList<string>>? runningProcesses = null)
    {
        _runningProcesses = runningProcesses ?? (() => RunningApps.List().Select(a => a.ProcessName).ToList());
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _templates = templates;
        _installedApps = installedApps;
        _device = device;

        _draft.Ended += OnDraftEnded;
        BuildFoci();
    }

    public bool IsAvailable => _templates != null;

    public ObservableCollection<TourFocusOption> Foci { get; } = new();
    public ObservableCollection<TourPartViewModel> Parts { get; } = new();

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private int _step;
    [ObservableProperty] private TourFocusOption? _selectedFocus;
    [ObservableProperty] private string _detectionText = "";
    [ObservableProperty] private string _noticedText = "";
    [ObservableProperty] private bool _hasNoticed;

    /// <summary>Also link the profile to the programs it is made for (off by default: nothing is linked behind the user's back).</summary>
    [ObservableProperty] private bool _linkApps;

    /// <summary>Raised with the new profile after it was kept (the Profiles page refreshes its list).</summary>
    public event Action<ProfileModel>? Kept;

    public bool IsWelcome => Step == WelcomeStep;
    public bool IsFocus => Step == FocusStep;
    public bool IsLooking => Step == LookingStep;
    public bool IsCompose => Step == ComposeStep;

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsWelcome));
        OnPropertyChanged(nameof(IsFocus));
        OnPropertyChanged(nameof(IsLooking));
        OnPropertyChanged(nameof(IsCompose));
    }

    // ── opening ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Offers the tour once, to someone whose deck has nothing set up yet (a new install). Never forced: "Not now" is remembered.
    /// Call it once at start.
    /// </summary>
    public void OfferIfFirstRun()
    {
        if (!IsAvailable || _profiles.GetAppSettings().TourState != TourState.NotOffered || !IsDeckUntouched()) return;
        Open(WelcomeStep);
    }

    /// <summary>Runs the tour again (Settings).</summary>
    [RelayCommand]
    public void Run() => Open(FocusStep);

    private bool IsDeckUntouched()
    {
        var all = _profiles.GetAllProfiles();
        if (all.Count != 1) return false;

        var only = _profiles.GetActiveProfile();
        static bool Empty(IEnumerable<MasterHub.Core.Profiles.KeyAssignmentModel>? keys) =>
            keys == null || keys.All(k => k.Action == MasterHub.Core.Actions.DeckActionType.None);

        return only.IsDefault && Empty(only.Keys) && (only.Pages ?? new()).All(p => Empty(p.Keys));
    }

    private void Open(int step)
    {
        if (!IsAvailable) return;

        DiscardDraftQuietly();   // a try-out that is open gives way to the tour
        _dropped.Clear();
        Parts.Clear();
        LinkApps = false;
        Step = step;
        IsOpen = true;

        _ = LookAroundAsync();
    }

    /// <summary>Looks at the running programs (off the UI thread: enumerating processes can take a moment) and suggests an answer.</summary>
    private async Task LookAroundAsync()
    {
        var installed = _installedApps?.Apps ?? Array.Empty<InstalledApp>();
        IReadOnlyList<string> running;
        try { running = await Task.Run(_runningProcesses); }
        catch { running = Array.Empty<string>(); }

        void Apply()
        {
            _context = new TourContext(running, installed.ToList());
            var suggested = TourAdvisor.Suggest(_context);
            foreach (var option in Foci) option.IsSuggested = option.Focus == suggested;
            SelectedFocus = Foci.FirstOrDefault(f => f.Focus == suggested) ?? Foci.First();

            var noticed = TourAdvisor.Noticed(_context);
            NoticedText = string.Join(", ", noticed);
            HasNoticed = noticed.Count > 0;
            DetectionText = DescribeModules();
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
    }

    private string DescribeModules()
    {
        var loc = LocalizationService.Instance;
        if (_device == null) return "";

        var found = new List<string>();
        if (_device.IsKeypadConnected) found.Add(loc.GetString("Tour_ModKeypad", "keypad"));
        if (_device.IsDialConnected) found.Add(loc.GetString("Tour_ModDial", "dial"));
        if (_device.IsKnobConnected) found.Add(loc.GetString("Tour_ModKnobs", "knobs"));
        if (_device.IsSliderConnected) found.Add(loc.GetString("Tour_ModFaders", "faders"));
        if (_device.IsRollerConnected) found.Add(loc.GetString("Tour_ModRollers", "rollers"));
        if (_device.IsBaseConnected) found.Add(loc.GetString("Tour_ModBase", "base"));

        return found.Count == 0
            ? loc.GetString("Tour_NoModules", "No deck is connected. That is fine: the draft is shown in the window and you can connect the deck later.")
            : string.Format(loc.GetString("Tour_Modules", "Connected: {0}."), string.Join(", ", found));
    }

    private void BuildFoci()
    {
        var loc = LocalizationService.Instance;
        Foci.Add(new TourFocusOption(TourFocus.Desk, loc.GetString("Tour_FocusDesk", "Everyday desk"), loc.GetString("Tour_FocusDeskDesc", "Media, volume, mute, screenshots, shortcuts.")));
        Foci.Add(new TourFocusOption(TourFocus.Streaming, loc.GetString("Tour_FocusStreaming", "Streaming"), loc.GetString("Tour_FocusStreamingDesc", "Go live, record, scenes, mic.")));
        Foci.Add(new TourFocusOption(TourFocus.Meetings, loc.GetString("Tour_FocusMeetings", "Calls and meetings"), loc.GetString("Tour_FocusMeetingsDesc", "Mute, camera, share screen, leave.")));
        Foci.Add(new TourFocusOption(TourFocus.Gaming, loc.GetString("Tour_FocusGaming", "Gaming"), loc.GetString("Tour_FocusGamingDesc", "Action bar for games, voice chat, recording.")));
        Foci.Add(new TourFocusOption(TourFocus.Monitoring, loc.GetString("Tour_FocusMonitoring", "System monitor"), loc.GetString("Tour_FocusMonitoringDesc", "CPU, memory, GPU, temperatures, network.")));
        Foci.Add(new TourFocusOption(TourFocus.SmartHome, loc.GetString("Tour_FocusSmartHome", "Smart home"), loc.GetString("Tour_FocusSmartHomeDesc", "Lights and switches in Home Assistant.")));
        SelectedFocus = Foci[0];
    }

    // ── steps ───────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Start() => Step = FocusStep;

    [RelayCommand]
    private void Next()
    {
        if (Step == FocusStep) Step = LookingStep;
    }

    [RelayCommand]
    private void Back()
    {
        if (Step == ComposeStep)
        {
            DiscardDraftQuietly();   // going back throws the draft away: nothing was kept
            Step = LookingStep;
        }
        else if (Step > FocusStep)
        {
            Step--;
        }
    }

    /// <summary>"Build my deck": makes a draft from the answer and shows it on the deck (nothing is saved).</summary>
    [RelayCommand]
    private void Build()
    {
        if (_templates == null || SelectedFocus == null) return;

        _template = _templates.GetAll().FirstOrDefault(t => t.Id == TourAdvisor.TemplateId(SelectedFocus.Focus));
        if (_template == null) return;

        _dropped.Clear();
        _building = true;
        try
        {
            Parts.Clear();
            foreach (var key in _template.Keys.OrderBy(k => k.Index))
            {
                string action = LocalizationService.Instance.GetString($"Action_{key.Action}", key.Action);
                Parts.Add(new TourPartViewModel(key.Index, key.Title, action, OnPartToggled));
            }
        }
        finally
        {
            _building = false;
        }

        ShowDraft();
        Step = ComposeStep;
    }

    private void OnPartToggled(TourPartViewModel part)
    {
        if (_building) return;

        if (part.Included) _dropped.Remove(part.KeyIndex);
        else _dropped.Add(part.KeyIndex);

        ShowDraft();
    }

    /// <summary>(Re)builds the draft from the template without the dropped keys and puts it on the deck.</summary>
    private void ShowDraft()
    {
        if (_templates == null || _template == null) return;

        string language = LocalizationService.Instance.CurrentLanguageCode?.Split('-')[0] ?? "en";
        string name = string.Format(LocalizationService.Instance.GetString("Tour_ProfileName", "Setup: {0}"), _template.NameFor(language));

        var profile = _templates.Build(_template, name);
        TourAdvisor.DropKeys(profile, _dropped);
        _draft.Start(profile, DraftMode.Preview);
    }

    /// <summary>Keeps the draft as a real profile (named after the answer) and finishes the tour.</summary>
    [RelayCommand]
    private void Keep()
    {
        var template = _template;
        ProfileModel? kept;
        _ending = true;
        try { kept = _draft.Keep(); }
        finally { _ending = false; }
        if (kept == null) return;

        if (LinkApps && template != null && template.Exes.Count > 0)
        {
            foreach (string exe in template.Exes)
                kept.SmartRules.Add(new SmartProfileRule { ProfileId = kept.Id, ProcessName = exe });
            _profiles.SaveProfile(kept);
        }

        Finish(TourState.Done);
        Kept?.Invoke(kept);
    }

    /// <summary>"Throw away" on the draft step.</summary>
    [RelayCommand]
    private void Cancel()
    {
        DiscardDraftQuietly();
        Finish(TourState.Dismissed);
    }

    /// <summary>"Not now" / the close button: the tour is not offered again by itself (Settings can run it).</summary>
    [RelayCommand]
    private void Skip()
    {
        DiscardDraftQuietly();
        Finish(TourState.Dismissed);
    }

    private void DiscardDraftQuietly()
    {
        if (!_draft.IsActive) return;

        _ending = true;
        try { _draft.Discard(); }
        finally { _ending = false; }
    }

    private void Finish(TourState state)
    {
        var settings = _profiles.GetAppSettings();
        if (settings.TourState != state)
        {
            settings.TourState = state;
            _profiles.SaveAppSettings(settings);
        }

        IsOpen = false;
        Parts.Clear();
        _template = null;
    }

    /// <summary>The draft ended by itself (another profile was chosen, a trial ran out): the compose step has nothing left to show.</summary>
    private void OnDraftEnded()
    {
        // a switch of profile (or anything else outside the tour) ended the draft: the compose step has nothing left to show
        if (!_ending && IsOpen && Step == ComposeStep && !_building) Finish(TourState.Dismissed);
    }
}

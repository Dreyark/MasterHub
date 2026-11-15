using System;
using System.Collections.Generic;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Apps;

namespace MasterHub.Core.Profiles.Auto;

/// <summary>What a suggestion is about.</summary>
public enum NudgeKind
{
    /// <summary>A program the user works in a lot has no profile.</summary>
    AppWithoutProfile = 0,

    /// <summary>A kind of module was attached for the first time.</summary>
    NewModule = 1,

    /// <summary>With press counting on: a key that was not pressed in a long time.</summary>
    UnusedKey = 2
}

/// <summary>One suggestion. <see cref="Key"/> identifies it, so it is never shown twice once it was answered.</summary>
/// <param name="Subject">The process name (<see cref="NudgeKind.AppWithoutProfile"/>), the module kind (<see cref="NudgeKind.NewModule"/>) or <c>profileId|keyIndex</c>.</param>
/// <param name="Display">What the text names: the program's name, the key's title.</param>
public sealed record Nudge(string Key, NudgeKind Kind, string Subject, string Display);

/// <summary>
/// Small suggestions without any AI (T-37): a program the user works in a lot has no profile, a kind of module was attached for
/// the first time, and (only with press counting on) a key that is never pressed. <b>One at a time, each only once</b>: a suggestion
/// that was accepted or dismissed never comes back. Nothing here changes anything by itself; it only asks.
/// </summary>
public interface INudgeService
{
    /// <summary>The suggestion to show now, or <c>null</c>.</summary>
    Nudge? Current { get; }

    event Action? Changed;

    /// <summary>Looks at everything again (call it now and then, e.g. when the profile changes or a program was used for a while).</summary>
    void Evaluate();

    /// <summary>The module kinds that are connected right now. A kind seen for the first time becomes a suggestion.</summary>
    void ModulesConnected(IEnumerable<string> kinds);

    /// <summary>The user did something with the suggestion: it is not shown again.</summary>
    void Answer(Nudge nudge);
}

/// <inheritdoc cref="INudgeService"/>
public sealed class NudgeService : INudgeService
{
    /// <summary>A program counts as "worked in" after this much time in front, on at least <see cref="MinDays"/> days.</summary>
    public const long MinSeconds = 30 * 60;

    public const int MinDays = 3;

    /// <summary>An unused key is only suggested once the profile was used this many times and counting ran this long.</summary>
    public const long MinProfilePresses = 40;

    public static readonly TimeSpan MinCountingTime = TimeSpan.FromDays(7);

    /// <summary>Windows' own programs and MasterHub itself: always in front, never "an app to make a profile for".</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "searchhost", "searchapp", "startmenuexperiencehost", "shellexperiencehost", "applicationframehost",
        "textinputhost", "lockapp", "systemsettings", "taskmgr", "dwm", "sihost", "msedgewebview2", "masterhub", "masterhub.avalonia", "idle"
    };

    private readonly IProfileService _profiles;
    private readonly IAssistSettingsStore _store;
    private readonly IAppUsageService _usage;
    private readonly IPressCounter _presses;
    private readonly IInstalledAppsService? _installed;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();
    private readonly List<string> _connected = new();
    private Nudge? _current;

    public NudgeService(IProfileService profiles, IAssistSettingsStore store, IAppUsageService usage, IPressCounter presses,
        IInstalledAppsService? installed = null, Func<DateTime>? now = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _presses = presses ?? throw new ArgumentNullException(nameof(presses));
        _installed = installed;
        _now = now ?? (() => DateTime.Now);
    }

    public event Action? Changed;

    public Nudge? Current
    {
        get { lock (_lock) return _current; }
    }

    public static string AppKey(string processName) => $"app:{AutoProfileService.Normalize(processName)}";
    public static string ModuleKey(string kind) => $"module:{kind}";
    public static string UnusedKeyKey(string profileId, int index) => $"unused:{profileId}:{index}";

    public void ModulesConnected(IEnumerable<string> kinds)
    {
        lock (_lock)
        {
            _connected.Clear();
            _connected.AddRange(kinds.Where(k => ModuleKinds.All.Contains(k)).Distinct());
        }

        var settings = _store.Read();
        if (settings.Enabled && settings.SeenModules == null)
        {
            // the first look: what is connected now was there before the assistant, so it is "known", not new
            List<string> known;
            lock (_lock) known = _connected.ToList();
            _store.Update(s => s.SeenModules ??= known);
        }

        Evaluate();
    }

    public void Evaluate()
    {
        var next = Pick();
        bool changed;
        lock (_lock)
        {
            changed = !Equals(_current, next);
            _current = next;
        }

        if (changed) Changed?.Invoke();
    }

    public void Answer(Nudge nudge)
    {
        if (nudge == null) return;

        _store.Update(s =>
        {
            if (!s.DismissedNudges.Contains(nudge.Key, StringComparer.OrdinalIgnoreCase)) s.DismissedNudges.Add(nudge.Key);
            if (nudge.Kind == NudgeKind.NewModule)
            {
                s.SeenModules ??= new List<string>();
                if (!s.SeenModules.Contains(nudge.Subject)) s.SeenModules.Add(nudge.Subject);
            }
        });

        Evaluate();
    }

    // ── the rules ───────────────────────────────────────────────────────────────────────────

    private Nudge? Pick()
    {
        var settings = _store.Read();
        if (!settings.Enabled) return null;

        bool Done(string key) => settings.DismissedNudges.Contains(key, StringComparer.OrdinalIgnoreCase);

        // a suggestion that is showing stays until it is answered, as long as it still applies
        var showing = Current;
        if (showing != null && !Done(showing.Key) && StillApplies(showing, settings)) return showing;

        return NewModule(settings, Done) ?? AppWithoutProfile(Done) ?? UnusedKey(settings, Done);
    }

    private bool StillApplies(Nudge nudge, AssistSettings settings) => nudge.Kind switch
    {
        NudgeKind.AppWithoutProfile => !HasProfileFor(nudge.Subject),
        NudgeKind.UnusedKey => settings.PressCounting,
        _ => true
    };

    /// <summary>Rule 2: a kind of module that was never attached before is connected now.</summary>
    private Nudge? NewModule(AssistSettings settings, Func<string, bool> done)
    {
        if (settings.SeenModules == null) return null;   // not looked at yet: the first look only records

        List<string> connected;
        lock (_lock) connected = _connected.ToList();

        foreach (string kind in connected)
        {
            if (settings.SeenModules.Contains(kind) || done(ModuleKey(kind))) continue;
            return new Nudge(ModuleKey(kind), NudgeKind.NewModule, kind, kind);
        }

        return null;
    }

    /// <summary>Rule 1: a program the user works in a lot has no profile.</summary>
    private Nudge? AppWithoutProfile(Func<string, bool> done)
    {
        var profiles = _profiles.GetAllProfiles();

        foreach (var (process, entry) in _usage.Snapshot().OrderByDescending(kv => kv.Value.Seconds).Select(kv => (kv.Key, kv.Value)))
        {
            if (entry.Seconds < MinSeconds || entry.Days < MinDays) continue;
            if (Ignored.Contains(process) || Ignored.Contains(AutoProfileService.Normalize(process))) continue;
            if (done(AppKey(process)) || HasProfileFor(process, profiles)) continue;

            return new Nudge(AppKey(process), NudgeKind.AppWithoutProfile, process, NameOf(process));
        }

        return null;
    }

    /// <summary>With press counting on: a key with an action that was not pressed once while the profile was used a lot.</summary>
    private Nudge? UnusedKey(AssistSettings settings, Func<string, bool> done)
    {
        if (!settings.PressCounting || !_presses.IsActive) return null;
        if (_presses.Since is not { } since || _now() - since < MinCountingTime) return null;

        var active = _profiles.GetActiveProfile();
        if (_profiles.ActiveDraft != null) return null;

        var counts = _presses.Get(active.Id);
        if (counts.Values.Sum() < MinProfilePresses) return null;

        // page 1 (the keys of the profile itself): the keys the user sees first
        string place = "p:0";
        foreach (var key in (active.Keys ?? new()).OrderBy(k => k.Index))
        {
            if (key.Action == DeckActionType.None) continue;
            if (counts.GetValueOrDefault(PressCounter.KeyId(place, key.Index)) > 0) continue;

            string id = UnusedKeyKey(active.Id, key.Index);
            if (done(id)) continue;

            string title = string.IsNullOrWhiteSpace(key.CustomTitle) ? key.Action.ToString() : key.CustomTitle;
            return new Nudge(id, NudgeKind.UnusedKey, $"{active.Id}|{key.Index}", title);
        }

        return null;
    }

    private bool HasProfileFor(string process) => HasProfileFor(process, _profiles.GetAllProfiles());

    private static bool HasProfileFor(string process, IEnumerable<ProfileModel> profiles)
    {
        string wanted = AutoProfileService.Normalize(process);
        return profiles.Any(p => p.SmartRules?.Any(r => AutoProfileService.Normalize(r.ProcessName) == wanted) == true);
    }

    private string NameOf(string process)
    {
        string wanted = AutoProfileService.Normalize(process);
        var app = _installed?.Apps.FirstOrDefault(a => AutoProfileService.Normalize(a.ProcessName) == wanted);
        return app?.Name ?? process;
    }
}

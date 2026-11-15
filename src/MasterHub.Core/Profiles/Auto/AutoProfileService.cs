using System;
using System.Collections.Generic;
using System.Linq;
using MasterHub.Core.Apps;
using MasterHub.Core.Profiles.Templates;

namespace MasterHub.Core.Profiles.Auto;

/// <summary>A profile for one program that is shown as a draft first (nothing saved); <see cref="Process"/> is what to link after it was kept.</summary>
public sealed record AppSketch(ProfileModel Profile, string Process);

/// <summary>
/// Profiles that make themselves (T-37), so nobody has to click one together for every program: at start, for the installed programs
/// that have a ready-made profile (OBS, Discord, Teams…); for a game, the first time it comes to the front.
/// <b>They arrive switched off</b> (the Smart Profile rules exist but are disabled), so a prepared profile never takes over the deck
/// until the user presses <i>Switch on</i>. Each program gets one only once: a profile the user deleted is not made again.
/// </summary>
public interface IAutoProfileService
{
    /// <summary>The master switch of the assistant is on.</summary>
    bool IsEnabled { get; }

    /// <summary>Raised after a profile was prepared, switched on or marked as seen.</summary>
    event Action? Changed;

    /// <summary>Prepares a profile for every installed program that has a ready-made profile and was not offered before. Returns how many were made.</summary>
    int PrepareForInstalledApps();

    /// <summary>A program came to the front: when it is an installed game that was never offered, a switched-off profile is made for it. Returns whether one was made.</summary>
    bool OnAppInFront(string processName);

    /// <summary>Turns the profile's rules on (and it stops being "ready"). It then switches in with its program like any linked profile.</summary>
    bool SwitchOn(string profileId);

    /// <summary>The profile's "new" mark goes away (the user has looked at it). With no ids: all of them.</summary>
    void MarkSeen(IEnumerable<string>? profileIds = null);

    /// <summary>Ids of prepared profiles the user has not looked at yet (those that still exist).</summary>
    IReadOnlyList<string> UnseenProfileIds();

    /// <summary>A profile for <paramref name="processName"/> to show as a draft: the ready-made profile of that program, or the everyday-desk keys.</summary>
    AppSketch? BuildSketch(string processName, string displayName);
}

/// <inheritdoc cref="IAutoProfileService"/>
public sealed class AutoProfileService : IAutoProfileService
{
    /// <summary>The ready-made profile a game gets.</summary>
    public const string GameTemplateId = "game-action-bar";

    /// <summary>The profile a program without a ready-made one gets as a sketch.</summary>
    public const string FallbackTemplateId = "everyday-desk";

    private readonly IProfileService _profiles;
    private readonly IProfileTemplateService _templates;
    private readonly IInstalledAppsService _installed;
    private readonly IAssistSettingsStore _store;
    private readonly Func<string> _language;
    private readonly object _lock = new();

    public AutoProfileService(IProfileService profiles, IProfileTemplateService templates, IInstalledAppsService installed,
        IAssistSettingsStore store, Func<string>? language = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _installed = installed ?? throw new ArgumentNullException(nameof(installed));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _language = language ?? (() => "en");
    }

    public event Action? Changed;

    public bool IsEnabled => _store.Enabled;

    public static string TemplateKey(string templateId) => $"template:{templateId}";
    public static string GameKey(string processName) => $"game:{Normalize(processName)}";

    /// <summary>A process name as it is compared: no <c>.exe</c>, lower case.</summary>
    internal static string Normalize(string? processName)
    {
        string name = (processName ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant();
    }

    /// <summary>A prepared profile that is still switched off ("Ready for your apps").</summary>
    public static bool IsReady(ProfileModel profile) =>
        profile.AutoMade && !profile.IsDefault && profile.SmartRules is { Count: > 0 } rules && rules.All(r => !r.IsEnabled);

    public int PrepareForInstalledApps()
    {
        if (!IsEnabled) return 0;

        int made = 0;
        lock (_lock)
        {
            var apps = _installed.Apps;
            if (apps.Count == 0) return 0;

            foreach (var template in _templates.GetAll().Where(t => t.Exes.Count > 0))
            {
                if (_store.Read().Offered.Contains(TemplateKey(template.Id), StringComparer.OrdinalIgnoreCase)) continue;

                // only the programs that are really installed here are linked
                var present = template.Exes
                    .Where(exe => apps.Any(a => Normalize(a.ProcessName) == Normalize(exe)))
                    .Select(Normalize)
                    .Distinct()
                    .ToList();
                if (present.Count == 0) continue;   // not installed (yet): looked at again at the next start

                var profile = _templates.Build(template, template.NameFor(Language()));
                Prepare(profile, present);
                _store.Update(s => { s.Offered.Add(TemplateKey(template.Id)); s.UnseenProfileIds.Add(profile.Id); });
                made++;
            }
        }

        if (made > 0) Changed?.Invoke();
        return made;
    }

    public bool OnAppInFront(string processName)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(processName)) return false;

        string process = Normalize(processName);
        var game = _installed.Apps.FirstOrDefault(a => a.IsGame && Normalize(a.ProcessName) == process);
        if (game == null) return false;

        lock (_lock)
        {
            string key = GameKey(process);
            if (_store.Read().Offered.Contains(key, StringComparer.OrdinalIgnoreCase)) return false;

            // the user already has a profile for it (made by hand): nothing is added next to it
            bool alreadyLinked = _profiles.GetAllProfiles().Any(p => p.SmartRules?.Any(r => Normalize(r.ProcessName) == process) == true);
            if (alreadyLinked)
            {
                _store.Update(s => s.Offered.Add(key));
                return false;
            }

            var template = _templates.GetAll().FirstOrDefault(t => t.Id == GameTemplateId);
            if (template == null) return false;

            var profile = _templates.Build(template, game.Name);
            Prepare(profile, new[] { process });
            _store.Update(s => { s.Offered.Add(key); s.UnseenProfileIds.Add(profile.Id); });
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Marks a built profile as prepared: rules present but off, and saved. Never made the active profile.</summary>
    private void Prepare(ProfileModel profile, IEnumerable<string> processes)
    {
        profile.AutoMade = true;
        profile.IsDefault = false;
        profile.SmartRules = processes
            .Select(p => new SmartProfileRule { ProfileId = profile.Id, ProcessName = p, IsEnabled = false })
            .ToList();
        _profiles.SaveProfile(profile);
    }

    public bool SwitchOn(string profileId)
    {
        var profile = _profiles.GetAllProfiles().FirstOrDefault(p => p.Id == profileId);
        if (profile == null || !profile.AutoMade) return false;

        foreach (var rule in profile.SmartRules ?? new()) rule.IsEnabled = true;
        profile.AutoMade = false;
        _profiles.SaveProfile(profile);

        _store.Update(s => s.UnseenProfileIds.Remove(profileId));
        Changed?.Invoke();
        return true;
    }

    public void MarkSeen(IEnumerable<string>? profileIds = null)
    {
        var ids = profileIds?.ToHashSet();
        _store.Update(s =>
        {
            if (ids == null) s.UnseenProfileIds.Clear();
            else s.UnseenProfileIds.RemoveAll(ids.Contains);
        });
        Changed?.Invoke();
    }

    public IReadOnlyList<string> UnseenProfileIds()
    {
        var unseen = _store.Read().UnseenProfileIds;
        if (unseen.Count == 0) return unseen;

        // a profile the user deleted before looking at it is no longer "new"
        var existing = _profiles.GetAllProfiles().Select(p => p.Id).ToHashSet();
        return unseen.Where(existing.Contains).ToList();
    }

    public AppSketch? BuildSketch(string processName, string displayName)
    {
        string process = Normalize(processName);
        if (process.Length == 0) return null;

        var template = _templates.GetAll().FirstOrDefault(t => t.Exes.Any(e => Normalize(e) == process))
                       ?? _templates.GetAll().FirstOrDefault(t => t.Id == FallbackTemplateId);
        if (template == null) return null;

        string name = string.IsNullOrWhiteSpace(displayName) ? processName : displayName.Trim();
        return new AppSketch(_templates.Build(template, name), process);
    }

    private string Language() => (_language() ?? "en").Split('-')[0];
}

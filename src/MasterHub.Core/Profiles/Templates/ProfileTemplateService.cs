using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MasterHub.Core.Actions;
using MasterHub.Core.SystemControl;

namespace MasterHub.Core.Profiles.Templates;

/// <summary>One key of a ready-made profile.</summary>
public sealed class TemplateKey
{
    public int Index { get; set; }
    public string Action { get; set; } = "";
    public string Parameter { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Background { get; set; } = "";

    /// <summary>For Hotkey keys: <c>Normal</c> or <c>ForGames</c>.</summary>
    public string SendMode { get; set; } = "Normal";
    public int HoldMs { get; set; }
}

/// <summary>A ready-made profile (T-10): data, not code. Authored for MasterHub's own actions.</summary>
public sealed class ProfileTemplate
{
    public string Id { get; set; } = "";
    public int Version { get; set; } = 1;

    /// <summary>Name and description by language code (<c>en</c> is the fallback).</summary>
    public Dictionary<string, string> Name { get; set; } = new();
    public Dictionary<string, string> Description { get; set; } = new();

    /// <summary>Executables (process names) of the apps this profile is for; linked as Smart Profile rules when the user asks for it.</summary>
    public List<string> Exes { get; set; } = new();

    /// <summary>Colour of the base's edge light.</summary>
    public string LedColor { get; set; } = "";

    public List<TemplateKey> Keys { get; set; } = new();

    public string NameFor(string language) => Pick(Name, language, Id);
    public string DescriptionFor(string language) => Pick(Description, language, "");

    private static string Pick(Dictionary<string, string> map, string language, string fallback) =>
        map.TryGetValue(language, out var v) && !string.IsNullOrWhiteSpace(v) ? v :
        map.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en) ? en : fallback;
}

/// <summary>Lists the ready-made profiles and turns one into a real profile.</summary>
public interface IProfileTemplateService
{
    IReadOnlyList<ProfileTemplate> GetAll();

    /// <summary>Creates and saves a new profile from a template. With <paramref name="linkApps"/> the template's apps become Smart Profile rules.</summary>
    ProfileModel Instantiate(ProfileTemplate template, string? name = null, bool linkApps = false);

    /// <summary>
    /// The profile a template describes, <b>not saved</b>. This is what a try-out or the setup tour shows as a draft (T-08);
    /// <see cref="Instantiate"/> is this plus saving it.
    /// </summary>
    ProfileModel Build(ProfileTemplate template, string? name = null, bool linkApps = false);
}

/// <inheritdoc cref="IProfileTemplateService"/>
public sealed class ProfileTemplateService : IProfileTemplateService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IProfileService _profiles;
    private readonly Lazy<IReadOnlyList<ProfileTemplate>> _templates;

    public ProfileTemplateService(IProfileService profiles, Func<IReadOnlyList<ProfileTemplate>>? source = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _templates = new Lazy<IReadOnlyList<ProfileTemplate>>(source ?? LoadEmbedded);
    }

    public IReadOnlyList<ProfileTemplate> GetAll() => _templates.Value;

    /// <summary>Reads every <c>Profiles/Templates/*.json</c> embedded in this assembly.</summary>
    public static IReadOnlyList<ProfileTemplate> LoadEmbedded()
    {
        var assembly = typeof(ProfileTemplateService).Assembly;
        var list = new List<ProfileTemplate>();

        foreach (string resource in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".Profiles.Templates.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var template = JsonSerializer.Deserialize<ProfileTemplate>(reader.ReadToEnd(), Json);
            if (template != null && !string.IsNullOrWhiteSpace(template.Id)) list.Add(template);
        }

        return list.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Everything wrong with a template (empty list = valid). Used by the tests and before a template is added.</summary>
    public static IReadOnlyList<string> Validate(ProfileTemplate t)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(t.Id)) problems.Add("id is missing");
        if (!t.Name.ContainsKey("en")) problems.Add("name needs an English text");
        if (!t.Description.ContainsKey("en")) problems.Add("description needs an English text");
        if (t.Keys.Count == 0) problems.Add("no keys");

        var seen = new HashSet<int>();
        foreach (var k in t.Keys)
        {
            string at = $"key {k.Index}";
            if (k.Index is < 0 or > 14) problems.Add($"{at}: index out of range");
            else if (!seen.Add(k.Index)) problems.Add($"{at}: duplicate index");

            if (!Enum.TryParse<DeckActionType>(k.Action, ignoreCase: false, out var action) || !Enum.IsDefined(action) || action == DeckActionType.None)
                problems.Add($"{at}: unknown action '{k.Action}'");
            else if (action == DeckActionType.StreamDeckPluginAction)
                problems.Add($"{at}: plugin actions cannot be part of a template");

            if (action == DeckActionType.Hotkey && HotkeyParser.Parse(k.Parameter) is not { MainKey: not 0 })
                problems.Add($"{at}: '{k.Parameter}' is not a hotkey");

            if (!Enum.TryParse<HotkeySendMode>(k.SendMode, out _)) problems.Add($"{at}: unknown send mode '{k.SendMode}'");
            else if (k.SendMode != nameof(HotkeySendMode.Normal) && action != DeckActionType.Hotkey)
                problems.Add($"{at}: a send mode only makes sense for a Hotkey");

            if (!string.IsNullOrEmpty(k.Background) && !System.Text.RegularExpressions.Regex.IsMatch(k.Background, "^#[0-9a-fA-F]{6}$"))
                problems.Add($"{at}: background is not #rrggbb");
            if (k.Title.Length > 14) problems.Add($"{at}: title longer than 14 characters");
            if (k.HoldMs != 0 && (k.HoldMs < HotkeySender.MinHoldMs || k.HoldMs > HotkeySender.MaxHoldMs)) problems.Add($"{at}: hold time out of range");
        }

        if (!string.IsNullOrEmpty(t.LedColor) && !System.Text.RegularExpressions.Regex.IsMatch(t.LedColor, "^#[0-9a-fA-F]{6}$"))
            problems.Add("ledColor is not #rrggbb");
        foreach (string exe in t.Exes)
            if (!System.Text.RegularExpressions.Regex.IsMatch(exe, @"^[A-Za-z0-9._ -]{1,64}$")) problems.Add($"exe '{exe}' is not a plain process name");

        return problems;
    }

    public ProfileModel Instantiate(ProfileTemplate template, string? name = null, bool linkApps = false)
    {
        var profile = Build(template, name, linkApps);
        _profiles.SaveProfile(profile);
        return profile;
    }

    public ProfileModel Build(ProfileTemplate template, string? name = null, bool linkApps = false)
    {
        var problems = Validate(template);
        if (problems.Count > 0) throw new InvalidOperationException($"Template '{template.Id}' is not valid: {problems[0]}");

        var profile = ProfileModel.CreateEmpty(string.IsNullOrWhiteSpace(name) ? template.NameFor("en") : name.Trim(), isDefault: false);

        foreach (var tk in template.Keys)
        {
            var key = profile.Keys.FirstOrDefault(k => k.Index == tk.Index) ?? new KeyAssignmentModel { Index = tk.Index };
            if (!profile.Keys.Contains(key)) profile.Keys.Add(key);

            key.Action = Enum.Parse<DeckActionType>(tk.Action);
            key.ActionParameter = tk.Parameter;
            key.CustomTitle = tk.Title;
            key.CustomSubtitle = tk.Subtitle;
            key.BackgroundColorHex = string.IsNullOrEmpty(tk.Background) ? "#000000" : tk.Background;
            key.HotkeySendMode = Enum.Parse<HotkeySendMode>(tk.SendMode);
            key.HotkeyHoldMs = tk.HoldMs;
        }

        // page 1 mirrors the top-level keys
        if (profile.Pages.Count == 0) profile.Pages.Add(new KeyPageModel { PageIndex = 0, Name = "Strona 1" });
        profile.Pages[0].Keys = profile.Keys.Select(k => k).ToList();

        if (!string.IsNullOrEmpty(template.LedColor)) { profile.LedColorHex = template.LedColor; profile.BaseBrightness = 100; }

        if (linkApps)
        {
            foreach (string exe in template.Exes)
                profile.SmartRules.Add(new SmartProfileRule { ProfileId = profile.Id, ProcessName = exe });
        }

        return profile;
    }
}

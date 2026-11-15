using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;

namespace MasterHub.Core.Profiles.Check;

/// <summary>What on this PC a control points at but cannot be found.</summary>
public enum CheckProblem
{
    /// <summary>A program path (<c>C:\…\app.exe</c>) that does not exist here.</summary>
    MissingProgram,
    /// <summary>A file or folder that does not exist here.</summary>
    MissingFile,
    /// <summary>A picture of the key (or the wallpaper) that is not on this PC.</summary>
    MissingPicture,
    /// <summary>A sound file of a soundboard key.</summary>
    MissingSound,
    /// <summary>An audio device (switch key or soundboard output) that is not connected.</summary>
    MissingAudioDevice,
    /// <summary>An OBS scene that the connected OBS does not have.</summary>
    MissingObsScene,
    /// <summary>A Home Assistant entity the connected server does not know.</summary>
    UnknownHaEntity,
    /// <summary>A Stream Deck plugin that is not installed.</summary>
    MissingPlugin,
    /// <summary>A bare name (<c>notepad</c>, <c>spotify</c>) that opens nothing: not on PATH and not an installed app.</summary>
    BareNameNotFound
}

/// <summary>One "check this" mark: where it is (page, key, gesture), what it is and what is missing.</summary>
public sealed record CheckFinding(string Page, int Key, string Gesture, DeckActionType Action, CheckProblem Problem, string Target)
{
    /// <summary>Text for lists: <c>page 1, key 3 (hold): MissingFile C:\x.txt</c>.</summary>
    public override string ToString() =>
        $"page {Page}, key {Key}{(Gesture.Length > 0 ? " (" + Gesture + ")" : "")}: {Problem} {Target}".TrimEnd();
}

/// <summary>
/// Lookups the check needs about this PC. A collection that is <c>null</c> means "not known now" (OBS not connected,
/// Home Assistant not set up…): those controls are then <b>not</b> marked, so a profile is never flagged for something
/// the app simply cannot see at the moment.
/// </summary>
public interface IProfileCheckEnvironment
{
    bool FileExists(string path);
    bool DirectoryExists(string path);

    /// <summary>Whether a bare program name (no folder) starts something: found on PATH, in App Paths or among installed apps.</summary>
    bool BareNameResolves(string name);

    IReadOnlyList<AudioDeviceInfo>? AudioDevices { get; }
    IReadOnlyCollection<string>? ObsScenes { get; }
    IReadOnlyCollection<string>? HomeAssistantEntities { get; }
    IReadOnlyCollection<string>? InstalledPlugins { get; }
}

/// <summary>
/// The import / profile check (T-34 #9): lists the controls of a profile that point at something this PC lacks – a program,
/// file or picture path, a sound, an audio device, an OBS scene, a Home Assistant entity, a plugin, or a bare name that opens
/// nothing – so a profile from another PC can be fixed in a minute. A pure function over <see cref="IProfileCheckEnvironment"/>:
/// it never changes the profile and never runs anything.
/// </summary>
public static class ProfileCheck
{
    public static IReadOnlyList<CheckFinding> Run(ProfileModel profile, IProfileCheckEnvironment env)
    {
        var findings = new List<CheckFinding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(CheckFinding f)
        {
            if (seen.Add(f.ToString())) findings.Add(f);
        }

        void Keys(string page, IEnumerable<KeyAssignmentModel>? keys)
        {
            if (keys == null) return;
            foreach (var k in keys)
            {
                int n = k.Index + 1;
                CheckAction(page, n, "", k.Action, k.ActionParameter, k, env, Add);
                if (k.Gestures?.Hold is { } hold) CheckAction(page, n, "hold", hold.Action, hold.ActionParameter, null, env, Add);
                if (k.Gestures?.Double is { } dbl) CheckAction(page, n, "double", dbl.Action, dbl.ActionParameter, null, env, Add);

                CheckPicture(page, n, k.Action, k.ImagePath, env, Add);
                CheckPicture(page, n, k.Action, k.ActiveImagePath, env, Add);
            }
        }

        // The top-level keys mirror page 1; both are checked and each mark is listed once.
        Keys("1", profile.Keys);
        for (int i = 0; i < (profile.Pages?.Count ?? 0); i++)
        {
            var page = profile.Pages![i];
            Keys((i + 1).ToString(), page.Keys);
            CheckPicture((i + 1).ToString(), 0, DeckActionType.None, page.PanoramicWallpaperPath, env, Add);
        }
        foreach (var folder in profile.Folders ?? new()) Keys(folder.Name, folder.Keys);
        CheckPicture("1", 0, DeckActionType.None, profile.PanoramicWallpaperPath, env, Add);

        return findings;
    }

    private static void CheckAction(string page, int key, string gesture, DeckActionType action, string? parameter,
        KeyAssignmentModel? model, IProfileCheckEnvironment env, Action<CheckFinding> add)
    {
        string p = (parameter ?? "").Trim();
        CheckFinding Mark(CheckProblem problem, string target) => new(page, key, gesture, action, problem, target);

        switch (action)
        {
            case DeckActionType.OpenApplication:
                if (CheckTarget(p, env, isProgram: true) is { } appProblem) add(Mark(appProblem, p));
                break;

            case DeckActionType.OpenFile:
                if (CheckTarget(p, env, isProgram: false) is { } fileProblem) add(Mark(fileProblem, p));
                break;

            case DeckActionType.AudioOutputSwitch when env.AudioDevices is { } devices:
            {
                var parsed = AudioSwitchParameter.Parse(p);
                var sameKind = devices.Where(d => d.IsCapture == parsed.Capture).ToList();
                foreach (var saved in new[] { parsed.A, parsed.B })
                {
                    if (string.IsNullOrWhiteSpace(saved.Id) && string.IsNullOrWhiteSpace(saved.Name)) continue;
                    if (AudioDeviceMatcher.Resolve(sameKind, saved) == null) add(Mark(CheckProblem.MissingAudioDevice, saved.Name));
                }
                break;
            }

            case DeckActionType.SoundPlay when model?.Sound is { } sound:
                if (!string.IsNullOrWhiteSpace(sound.FilePath) && !env.FileExists(Expand(sound.FilePath)))
                    add(Mark(CheckProblem.MissingSound, sound.FilePath));
                if (!string.IsNullOrWhiteSpace(sound.DeviceId) && env.AudioDevices is { } outputs &&
                    !outputs.Any(d => string.Equals(d.Id, sound.DeviceId, StringComparison.OrdinalIgnoreCase)))
                    add(Mark(CheckProblem.MissingAudioDevice, sound.DeviceId));
                break;

            case DeckActionType.ObsSceneSwitch when env.ObsScenes is { } scenes && p.Length > 0:
                if (!scenes.Contains(p, StringComparer.Ordinal)) add(Mark(CheckProblem.MissingObsScene, p));
                break;

            case DeckActionType.HomeAssistantToggle or DeckActionType.HomeAssistantTurnOn or DeckActionType.HomeAssistantTurnOff
                when env.HomeAssistantEntities is { } entities && p.Length > 0:
                if (!entities.Contains(p, StringComparer.OrdinalIgnoreCase)) add(Mark(CheckProblem.UnknownHaEntity, p));
                break;

            case DeckActionType.StreamDeckPluginAction when env.InstalledPlugins is { } plugins:
                string uuid = model?.StreamDeckPluginUuid ?? "";
                if (uuid.Length > 0 && !plugins.Contains(uuid, StringComparer.OrdinalIgnoreCase)) add(Mark(CheckProblem.MissingPlugin, uuid));
                break;

            case DeckActionType.ConditionalAction:
            {
                var c = ConditionalActionModel.Parse(parameter);
                CheckAction(page, key, gesture, c.IfTrueAction, c.IfTrueParameter, null, env, add);
                CheckAction(page, key, gesture, c.IfFalseAction, c.IfFalseParameter, null, env, add);
                break;
            }
        }
    }

    /// <summary>The problem of a program / file target, or <c>null</c> when it is fine or cannot be checked (a link, a Store app).</summary>
    internal static CheckProblem? CheckTarget(string target, IProfileCheckEnvironment env, bool isProgram)
    {
        if (target.Length == 0) return null;
        string t = Expand(Unquote(target));
        if (t.Contains("://", StringComparison.Ordinal) || t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return null;

        bool hasFolder = t.Contains('\\') || t.Contains('/') || (t.Length >= 2 && t[1] == ':');
        if (hasFolder)
        {
            if (env.FileExists(t) || env.DirectoryExists(t)) return null;
            return isProgram ? CheckProblem.MissingProgram : CheckProblem.MissingFile;
        }
        return env.BareNameResolves(t) ? null : CheckProblem.BareNameNotFound;
    }

    private static void CheckPicture(string page, int key, DeckActionType action, string? path, IProfileCheckEnvironment env, Action<CheckFinding> add)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!env.FileExists(Expand(path))) add(new CheckFinding(page, key, "", action, CheckProblem.MissingPicture, path));
    }

    private static string Unquote(string s) => s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    private static string Expand(string s)
    {
        try
        {
            return Environment.ExpandEnvironmentVariables(s);
        }
        catch
        {
            return s;
        }
    }
}

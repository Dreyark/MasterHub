using System;
using System.Collections.Generic;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Profiles.Templates;

namespace MasterHub.Core.Profiles.Auto;

/// <summary>The kinds of module the assistant knows about (T-37). The ids are what is stored in the settings.</summary>
public static class ModuleKinds
{
    public const string Keys = "keys";
    public const string Knobs = "knobs";
    public const string Faders = "faders";
    public const string Rollers = "rollers";
    public const string Dial = "dial";
    public const string Base = "base";

    public static readonly IReadOnlyList<string> All = new[] { Keys, Knobs, Faders, Rollers, Dial, Base };
}

/// <summary>
/// "A sketch for the module you just attached" (T-37): the user's own profile with sensible assignments on the controls of that
/// module that have nothing yet. Controls that already do something are never touched. The result is a draft to look at; nothing is saved.
/// </summary>
public static class ModuleSketch
{
    // These strings are what the fader/knob view models parse back, so they must contain the markers they look for (🔊, 🎙️).
    private const string MasterTarget = "🔊 Master volume";
    private const string MicTarget = "🎙️ Mic";

    /// <summary>
    /// The sketch for <paramref name="kind"/> built on <paramref name="profile"/> (a copy the caller owns), or <c>null</c> when there
    /// is nothing to suggest (every control of that module already has a job).
    /// </summary>
    public static ProfileModel? Build(string kind, ProfileModel profile, IProfileTemplateService? templates = null, string language = "en")
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        return kind switch
        {
            ModuleKinds.Faders => Faders(profile),
            ModuleKinds.Knobs => Knobs(profile),
            ModuleKinds.Rollers => Rollers(profile),
            ModuleKinds.Dial => Dial(profile),
            ModuleKinds.Base => Edge(profile),
            ModuleKinds.Keys => KeysFromTemplate(profile, templates),
            _ => null
        };
    }

    private static ProfileModel? Faders(ProfileModel profile)
    {
        profile.Sliders ??= new List<SliderAssignmentModel>();
        var free = profile.Sliders.Where(s => s.TargetType == AudioTargetType.None).OrderBy(s => s.Index).Take(2).ToList();
        if (free.Count == 0) return null;

        (AudioTargetType Type, string Name, string Color)[] plan =
        {
            (AudioTargetType.MasterPlayback, MasterTarget, "#38BDF8"),
            (AudioTargetType.MasterRecording, MicTarget, "#00E5A3")
        };

        for (int i = 0; i < free.Count; i++)
        {
            free[i].TargetType = plan[i].Type;
            free[i].TargetProcessName = "";
            free[i].DisplayTarget = plan[i].Name;
            free[i].ColorHex = plan[i].Color;
            free[i].Brightness = 100;
        }

        return profile;
    }

    private static ProfileModel? Knobs(ProfileModel profile)
    {
        profile.Knobs ??= new List<KnobAssignmentModel>();
        var free = profile.Knobs.Where(k => k.RotateAction == RollerActionType.None && k.PressAction == DeckActionType.None).OrderBy(k => k.Index).ToList();
        if (free.Count == 0) return null;

        RollerActionType[] plan = { RollerActionType.MasterVolume, RollerActionType.MediaTrack, RollerActionType.ScrollVertical };
        for (int i = 0; i < Math.Min(free.Count, plan.Length); i++) free[i].RotateAction = plan[i];
        return profile;
    }

    private static ProfileModel? Rollers(ProfileModel profile)
    {
        profile.Rollers ??= new List<RollerAssignmentModel>();
        var free = profile.Rollers.Where(r => r.Action == RollerActionType.None).OrderBy(r => r.Index).ToList();
        if (free.Count == 0) return null;

        RollerActionType[] plan = { RollerActionType.ScrollVertical, RollerActionType.MasterVolume };
        for (int i = 0; i < Math.Min(free.Count, plan.Length); i++)
        {
            free[i].Action = plan[i];
            free[i].ColorHex = "#38BDF8";
            free[i].Brightness = 100;
        }

        return profile;
    }

    private static ProfileModel? Dial(ProfileModel profile)
    {
        profile.Dial ??= new DialAssignmentModel();
        // an image the user chose is theirs; only an unused dial gets a clock
        if (profile.Dial.DisplayWidget != "Image" || !string.IsNullOrWhiteSpace(profile.Dial.ImagePath)) return null;
        if (profile.Dial.RotateAction != RollerActionType.None || profile.Dial.PressAction != DeckActionType.None) return null;

        profile.Dial.DisplayWidget = "Clock";
        profile.Dial.RotateAction = RollerActionType.MasterVolume;
        return profile;
    }

    private static ProfileModel? Edge(ProfileModel profile)
    {
        // an edge light that is already on is the user's choice
        if (profile.BaseBrightness > 0 && !string.Equals(profile.LedColorHex, "#000000", StringComparison.OrdinalIgnoreCase)) return null;

        profile.LedColorHex = "#00E5A3";
        profile.BaseBrightness = 60;
        return profile;
    }

    private static ProfileModel? KeysFromTemplate(ProfileModel profile, IProfileTemplateService? templates)
    {
        if (templates == null) return null;

        // a keypad that already has actions is the user's own work
        bool anyAction(IEnumerable<KeyAssignmentModel>? keys) => keys != null && keys.Any(k => k.Action != DeckActionType.None);
        if (anyAction(profile.Keys) || (profile.Pages ?? new()).Any(p => anyAction(p.Keys))) return null;

        var template = templates.GetAll().FirstOrDefault(t => t.Id == "everyday-desk");
        if (template == null) return null;

        // only the keypad comes from the template; knobs, faders and everything else stay the user's own
        var built = templates.Build(template, profile.Name);
        profile.Keys = built.Keys;
        profile.Pages = built.Pages;
        return profile;
    }
}

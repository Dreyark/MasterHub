using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Api;

/// <summary>
/// Checks API writes before anything is changed (all-or-nothing): control ids, action names, hotkeys, colours.
/// Everything wrong is reported at once; warnings (a label too long for the LCD) accompany a successful write.
/// </summary>
public static partial class ProfileValidator
{
    public const int KeyCount = 15;
    public const int MaxTitleLength = 64;
    public const int SoftTitleLength = 12;

    [GeneratedRegex(@"^keys\.(\d{1,2})(?::(hold|double))?$")]
    private static partial Regex KeyIdRegex();

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"^(sliders|faders|rollers|knobs)\.\d{1,2}$|^dial(\.\d{1,2})?$")]
    private static partial Regex OtherControlRegex();

    /// <summary>Parses <c>keys.N</c>, <c>keys.N:hold</c> or <c>keys.N:double</c>; <c>null</c> when it is not a valid key id.</summary>
    public static (int Index, KeyGesture Gesture)? ParseControl(string? control)
    {
        var m = KeyIdRegex().Match(control ?? "");
        if (!m.Success) return null;
        int index = int.Parse(m.Groups[1].Value);
        if (index >= KeyCount) return null;

        var gesture = m.Groups[2].Value switch
        {
            "hold" => KeyGesture.Hold,
            "double" => KeyGesture.Double,
            _ => KeyGesture.Press
        };
        return (index, gesture);
    }

    /// <summary>The key index of a control id (<c>keys.N</c> with an optional gesture suffix); <c>null</c> when invalid.</summary>
    public static int? ParseKeyIndex(string? control) => ParseControl(control)?.Index;

    /// <param name="changes">The requested changes.</param>
    /// <param name="pageCount">How many pages the profile has (a change may name a page from 1 to this).</param>
    /// <param name="limits">Set when the change comes from an AI assistant: the rules of <see cref="AssistantGuard"/> apply on top (T-26).</param>
    public static ValidationResult Validate(IReadOnlyList<ControlChange>? changes, int pageCount = 1, AssistantLimits? limits = null)
    {
        var result = new ValidationResult();
        if (changes == null || changes.Count == 0)
        {
            result.Errors.Add("controls: at least one change is required.");
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < changes.Count; i++)
        {
            var c = changes[i];
            string at = $"controls[{i}]";

            if (string.IsNullOrWhiteSpace(c.Control))
            {
                result.Errors.Add($"{at}.control is required.");
                continue;
            }

            if (ParseKeyIndex(c.Control) is null)
            {
                result.Errors.Add(OtherControlRegex().IsMatch(c.Control)
                    ? $"{at}.control '{c.Control}' is not supported yet (the API currently edits keys.0 … keys.{KeyCount - 1})."
                    : $"{at}.control '{c.Control}' is not a valid control id (expected keys.0 … keys.{KeyCount - 1}).");
            }
            else if (!seen.Add(c.Control))
            {
                result.Errors.Add($"{at}.control '{c.Control}' appears more than once.");
            }

            if (c.Page is { } page && (page < 1 || page > Math.Max(1, pageCount)))
                result.Errors.Add($"{at}.page {page} does not exist (the profile has {Math.Max(1, pageCount)} page(s), numbered from 1).");

            if (c.Clear)
            {
                if (!string.IsNullOrEmpty(c.Action))
                    result.Errors.Add($"{at}: 'clear' cannot be combined with 'action'.");
                continue;
            }

            // A change without an action only changes how the key looks (a label, a colour) and keeps what it does.
            if (IsLookOnly(c)) ValidateLookOnly(c, at, result);
            else ValidateAction(c, at, result);
            if (limits != null && !IsLookOnly(c) && ParseControl(c.Control) is { } parsed &&
                Enum.TryParse<DeckActionType>(c.Action, ignoreCase: true, out var asked) && Enum.IsDefined(asked) && !int.TryParse(c.Action, out _))
                AssistantGuard.Check(c, asked, parsed.Gesture, at, limits, result);
            if (ParseControl(c.Control) is { Gesture: not KeyGesture.Press } &&
                (c.Title != null || c.Subtitle != null || c.BackgroundColorHex != null || c.TextColorHex != null))
            {
                result.Errors.Add($"{at}: title, subtitle and colours belong to the tap binding; '{c.Control}' only takes 'action' and 'parameter'.");
            }
            ValidateColor(c.BackgroundColorHex, $"{at}.backgroundColorHex", result);
            ValidateColor(c.TextColorHex, $"{at}.textColorHex", result);
            ValidateText(c.Title, $"{at}.title", result);
            ValidateText(c.Subtitle, $"{at}.subtitle", result);
        }

        return result;
    }

    /// <summary>A tap-key change with no action but with something to show: the key keeps its action.</summary>
    private static bool IsLookOnly(ControlChange c) =>
        !c.Clear && string.IsNullOrWhiteSpace(c.Action) && string.IsNullOrEmpty(c.Parameter) &&
        ParseControl(c.Control) is { Gesture: KeyGesture.Press } &&
        (c.Title != null || c.Subtitle != null || !string.IsNullOrEmpty(c.BackgroundColorHex) || !string.IsNullOrEmpty(c.TextColorHex));

    private static void ValidateLookOnly(ControlChange c, string at, ValidationResult result)
    {
        ValidateColor(c.BackgroundColorHex, $"{at}.backgroundColorHex", result);
        ValidateColor(c.TextColorHex, $"{at}.textColorHex", result);
        ValidateText(c.Title, $"{at}.title", result);
        ValidateText(c.Subtitle, $"{at}.subtitle", result);
    }

    /// <summary>Checks the body of <c>POST /api/lights</c>.</summary>
    public static (LightsMode Mode, string? Color, ValidationResult Result) ValidateLights(LightsRequest? request)
    {
        var result = new ValidationResult();
        if (request == null || string.IsNullOrWhiteSpace(request.Mode))
        {
            result.Errors.Add("mode is required: profile, off, color or rainbow.");
            return (LightsMode.Profile, null, result);
        }

        if (!Enum.TryParse<LightsMode>(request.Mode, ignoreCase: true, out var mode) || !Enum.IsDefined(mode) || int.TryParse(request.Mode, out _))
        {
            result.Errors.Add($"mode '{request.Mode}' is not known (profile, off, color or rainbow).");
            return (LightsMode.Profile, null, result);
        }

        if (mode == LightsMode.Color)
        {
            if (string.IsNullOrEmpty(request.Color)) result.Errors.Add("color is required for mode 'color' (a #rrggbb colour).");
            else ValidateColor(request.Color, "color", result);
        }
        else if (!string.IsNullOrEmpty(request.Color))
        {
            result.Errors.Add($"color only belongs to mode 'color', not '{request.Mode}'.");
        }

        return (mode, mode == LightsMode.Color ? request.Color : null, result);
    }

    private static void ValidateAction(ControlChange c, string at, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(c.Action))
        {
            result.Errors.Add($"{at}.action is required (or set 'clear': true).");
            return;
        }

        if (!Enum.TryParse<DeckActionType>(c.Action, ignoreCase: true, out var action) || !Enum.IsDefined(action) || int.TryParse(c.Action, out _))
        {
            result.Errors.Add($"{at}.action '{c.Action}' is not a known action.");
            return;
        }

        if (action == DeckActionType.None)
            result.Errors.Add($"{at}.action 'None' – use 'clear': true to empty a key.");
        else if (action == DeckActionType.StreamDeckPluginAction)
            result.Errors.Add($"{at}.action 'StreamDeckPluginAction' cannot be set through the API (it needs a plugin and its settings).");

        if (action is DeckActionType.Hotkey && !IsParseableHotkey(c.Parameter))
            result.Errors.Add($"{at}.parameter '{c.Parameter}' is not a hotkey (expected e.g. 'Ctrl+Shift+M').");

        if (action is DeckActionType.HttpRequest && Actions.HttpAction.Parse(c.Parameter) == null)
            result.Errors.Add($"{at}.parameter must be '[METHOD] http(s)://… [| body]' for HttpRequest.");
        if (action is DeckActionType.OpenWebsite && !IsHttpUrl(c.Parameter))
            result.Errors.Add($"{at}.parameter must be an http(s) URL for OpenWebsite.");

        if (action is DeckActionType.OpenApplication or DeckActionType.OpenFile && string.IsNullOrWhiteSpace(c.Parameter))
            result.Errors.Add($"{at}.parameter (a path) is required for {action}.");
    }

    private static void ValidateColor(string? value, string at, ValidationResult result)
    {
        if (!string.IsNullOrEmpty(value) && !ColorRegex().IsMatch(value))
            result.Errors.Add($"{at} '{value}' is not a #rrggbb colour.");
    }

    private static void ValidateText(string? value, string at, ValidationResult result)
    {
        if (value == null) return;
        if (value.Length > MaxTitleLength)
            result.Errors.Add($"{at} is longer than {MaxTitleLength} characters.");
        else if (value.Length > SoftTitleLength)
            result.Warnings.Add($"{at} '{value}' is longer than {SoftTitleLength} characters and may be cut off on the LCD.");
    }

    /// <summary>Modifiers and one key, joined by '+': <c>Ctrl+Alt+F5</c>, <c>Win+D</c>, <c>Ctrl++</c> is not supported.</summary>
    public static bool IsParseableHotkey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(p => p.Length == 0 || p.Length > 20)) return false;

        string[] modifiers = { "ctrl", "control", "alt", "shift", "win", "windows", "cmd" };
        string key = parts[^1];
        return parts[..^1].All(p => modifiers.Contains(p.ToLowerInvariant())) && !modifiers.Contains(key.ToLowerInvariant());
    }

    private static bool IsHttpUrl(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Applies already validated changes to the profile (page 1 / the keys that the Studio edits at the top level).</summary>
    public static void Apply(ProfileModel profile, IReadOnlyList<ControlChange> changes)
    {
        profile.Keys ??= new List<KeyAssignmentModel>();
        profile.Pages ??= new List<KeyPageModel>();
        if (profile.Pages.Count == 0)
        {
            profile.Pages.Add(new KeyPageModel { PageIndex = 0, Name = "Strona 1", Keys = new List<KeyAssignmentModel>(profile.Keys) });
        }

        foreach (var change in changes)
        {
            var (index, gesture) = ParseControl(change.Control)!.Value;

            // The profile keeps page 1 twice (top-level Keys and Pages[0].Keys); both must change together.
            // Any other page is only in Pages[n].
            int page = Math.Clamp(change.Page ?? 1, 1, Math.Max(1, profile.Pages.Count));
            var lists = page == 1 ? new[] { profile.Keys, profile.Pages[0].Keys } : new[] { profile.Pages[page - 1].Keys };
            foreach (var list in lists)
            {
                var key = list.FirstOrDefault(k => k.Index == index);
                if (key == null)
                {
                    key = new KeyAssignmentModel { Index = index };
                    list.Add(key);
                }

                if (gesture != KeyGesture.Press)
                {
                    ApplyGesture(key, gesture, change);
                    continue;
                }

                if (change.Clear)
                {
                    key.Action = DeckActionType.None;
                    key.ActionParameter = "";
                    key.CustomTitle = "";
                    key.CustomSubtitle = "";
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(change.Action))
                {
                    key.Action = Enum.Parse<DeckActionType>(change.Action, ignoreCase: true);
                    key.ActionParameter = change.Parameter ?? "";
                }
                if (change.Title != null) key.CustomTitle = change.Title;
                if (change.Subtitle != null) key.CustomSubtitle = change.Subtitle;
                if (!string.IsNullOrEmpty(change.BackgroundColorHex)) key.BackgroundColorHex = change.BackgroundColorHex;
                if (!string.IsNullOrEmpty(change.TextColorHex)) key.TextColorHex = change.TextColorHex;
            }
        }
    }

    /// <summary>Sets or clears the hold / double binding of a key (<c>keys.N:hold</c>, <c>keys.N:double</c>).</summary>
    private static void ApplyGesture(KeyAssignmentModel key, KeyGesture gesture, ControlChange change)
    {
        var binding = change.Clear
            ? null
            : new KeyGestureBinding
            {
                Action = Enum.Parse<DeckActionType>(change.Action!, ignoreCase: true),
                ActionParameter = change.Parameter ?? ""
            };

        key.Gestures ??= new KeyGestures();
        if (gesture == KeyGesture.Hold) key.Gestures.Hold = binding;
        else key.Gestures.Double = binding;

        if (key.Gestures.IsEmpty) key.Gestures = null; // a key without extra gestures stays exactly as in an old profile
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MasterHub.Core.Actions;

namespace MasterHub.Core.Api;

/// <summary>What an AI assistant (the MCP server) may put on a key (T-26). It is enforced here, in code – never by a prompt.</summary>
/// <param name="MorePower">The user switched on "more power": the assistant may also use actions that call arbitrary services or open scripts.</param>
public sealed record AssistantLimits(bool MorePower = false)
{
    /// <summary>The header a client sends to say it is an assistant: <c>assistant</c> or <c>assistant-power</c>.</summary>
    public const string Header = "X-MasterHub-Client";

    public static AssistantLimits? FromHeader(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "assistant" => new AssistantLimits(false),
        "assistant-power" => new AssistantLimits(true),
        _ => null
    };
}

/// <summary>
/// The rules for what an assistant may set (conclusion of two design reviews of the product we compared with):
/// <list type="bullet">
/// <item>never an action that calls any service or runs anything the assistant names (unless "more power" is on);</item>
/// <item>risky targets – scripts, installers, shells – are never opened (unless "more power" is on);</item>
/// <item>anything that closes, quits, deletes or shuts down is accepted <b>only on a hold</b> (<c>keys.N:hold</c>), never on a plain press.</item>
/// </list>
/// A refusal says why and what would be accepted, so the model can retry correctly.
/// </summary>
public static class AssistantGuard
{
    /// <summary>Actions that call an arbitrary service the assistant names.</summary>
    // a URL call can reach any service, and a multi-action can hide any step: only with "more power" (T-17)
    private static readonly HashSet<DeckActionType> PowerOnly = new() { DeckActionType.HomeAssistantCallService, DeckActionType.HttpRequest, DeckActionType.MultiAction };

    /// <summary>Actions that close, quit, delete or shut down: hold only.</summary>
    private static readonly HashSet<DeckActionType> HoldOnly = new()
    {
        DeckActionType.SystemShutdown, DeckActionType.SystemRestart, DeckActionType.SystemSleep,
        DeckActionType.CloseApplication, DeckActionType.SystemVirtualDesktopClose
    };

    private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".reg", ".scr", ".com", ".hta", ".cpl", ".jar", ".dll", ".lnk"
    };

    private static readonly HashSet<string> RiskyPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "regedit", "rundll32", "regsvr32", "msiexec", "bash", "wsl"
    };

    /// <summary>Shortcuts that close or delete things (modifiers sorted, joined with '+').</summary>
    private static readonly HashSet<string> DestructiveHotkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "alt+f4", "ctrl+w", "ctrl+f4", "ctrl+q", "delete", "shift+delete", "ctrl+shift+delete", "ctrl+shift+w", "alt+shift+f4", "alt+ctrl+delete"
    };

    public static void Check(ControlChange change, DeckActionType action, KeyGesture gesture, string at, AssistantLimits limits, ValidationResult result)
    {
        if (PowerOnly.Contains(action) && !limits.MorePower)
        {
            result.Errors.Add($"{at}: the action '{action}' is not allowed for an assistant (it can call any service). " +
                              "Use HomeAssistantToggle, HomeAssistantTurnOn or HomeAssistantTurnOff with an entity id instead, or ask the user to switch on \"more power\".");
        }

        if (action is DeckActionType.OpenApplication or DeckActionType.OpenFile && !limits.MorePower && IsRiskyTarget(change.Parameter))
        {
            result.Errors.Add($"{at}.parameter '{change.Parameter}' is a script, installer or shell: an assistant may not put it on a key. " +
                              "Choose a normal program or a document, or ask the user to switch on \"more power\".");
        }

        bool destructive = HoldOnly.Contains(action) || (action == DeckActionType.Hotkey && IsDestructiveHotkey(change.Parameter));
        if (destructive && gesture != KeyGesture.Hold)
        {
            string id = change.Control.Split(':')[0];
            result.Errors.Add($"{at}: '{action}'{(action == DeckActionType.Hotkey ? $" ({change.Parameter})" : "")} closes or shuts something down, so it is only accepted on a hold. " +
                              $"Use the control id '{id}:hold' instead of '{change.Control}'.");
        }
    }

    public static bool IsRiskyTarget(string? target)
    {
        string t = (target ?? "").Trim().Trim('"');
        if (t.Length == 0) return false;

        // The target may carry arguments ("cmd.exe /c del *") or have spaces inside a path: look at the whole text and at its first word.
        return IsRiskyFile(t) || IsRiskyFile(t.Split(' ', 2)[0]);
    }

    private static bool IsRiskyFile(string file)
    {
        string name = Path.GetFileName(file.TrimEnd('\\', '/'));
        return RiskyExtensions.Contains(Path.GetExtension(name)) || RiskyPrograms.Contains(Path.GetFileNameWithoutExtension(name));
    }

    public static bool IsDestructiveHotkey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries).Select(p => p.ToLowerInvariant()).Select(p => p switch
        {
            "control" => "ctrl",
            "windows" or "cmd" => "win",
            _ => p
        }).ToList();

        string key = parts[^1];
        var mods = parts[..^1].OrderBy(m => m, StringComparer.Ordinal);
        return DestructiveHotkeys.Contains(string.Join('+', mods.Append(key)));
    }
}

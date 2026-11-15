using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MasterHub.Core.Status;

/// <summary>Body of <c>POST /api/state</c>: an app pushes what it reports (no server of its own needed).</summary>
public sealed class StateRequest
{
    public string Source { get; set; } = "";
    public List<StateItemRequest> Items { get; set; } = new();
}

public sealed class StateItemRequest
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string? Label { get; set; }
    public string? Text { get; set; }
    public string? Value { get; set; }
}

/// <summary>Body of <c>POST /api/agent</c> – what a Claude Code hook sends. Nothing else is read, so nothing else can leak.</summary>
public sealed class AgentRequest
{
    public string Session { get; set; } = "";
    public string? Project { get; set; }
    public string Event { get; set; } = "";
    public string? Tool { get; set; }
}

/// <summary>Checks what apps push before it reaches the board. All or nothing: one bad item refuses the whole request.</summary>
public static partial class StatusRequests
{
    public const int MaxItems = 50;
    public const int MaxId = 64;
    public const int MaxLabel = 48;
    public const int MaxText = 120;
    public const int MaxValue = 32;

    /// <summary>What a pushed state stays on the board without a refresh.</summary>
    public static readonly TimeSpan PushedLifetime = TimeSpan.FromHours(24);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,31}$")]
    private static partial Regex SourceRegex();

    public static (IReadOnlyList<StatusItem>? Items, string? Source, IReadOnlyList<string> Errors) Validate(StateRequest? request, DateTime now)
    {
        var errors = new List<string>();
        if (request == null) return (null, null, new[] { "the body is missing" });

        string source = (request.Source ?? "").Trim().ToLowerInvariant();
        if (!SourceRegex().IsMatch(source)) errors.Add("source must be 1-32 characters: letters, digits, '.', '_' or '-'");
        if (source == AgentTracker.Source) errors.Add($"the source '{AgentTracker.Source}' belongs to Claude Code and is set through /api/agent");
        if (request.Items == null || request.Items.Count > MaxItems) errors.Add($"items: at most {MaxItems}");

        var items = new List<StatusItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; request.Items != null && i < Math.Min(request.Items.Count, MaxItems); i++)
        {
            var item = request.Items[i];
            string where = $"items[{i}]";
            string id = (item.Id ?? "").Trim();

            if (id.Length == 0 || id.Length > MaxId) errors.Add($"{where}.id must be 1-{MaxId} characters");
            else if (!ids.Add(id)) errors.Add($"{where}.id '{id}' appears twice");

            if (!StatusBoard.TryParseState(item.State, out var state)) errors.Add($"{where}.state must be one of: waiting, working, done, error, idle, offline");
            if ((item.Label?.Length ?? 0) > MaxLabel) errors.Add($"{where}.label is longer than {MaxLabel}");
            if ((item.Text?.Length ?? 0) > MaxText) errors.Add($"{where}.text is longer than {MaxText}");
            if ((item.Value?.Length ?? 0) > MaxValue) errors.Add($"{where}.value is longer than {MaxValue}");

            items.Add(new StatusItem(source, id, state, Clip(item.Label), Clip(item.Text), Clip(item.Value), now));
        }

        return errors.Count > 0 ? (null, null, errors) : (items, source, errors);
    }

    public static (AgentEvent? Event, IReadOnlyList<string> Errors) Validate(AgentRequest? request)
    {
        var errors = new List<string>();
        if (request == null) return (null, new[] { "the body is missing" });

        if (string.IsNullOrWhiteSpace(request.Session)) errors.Add("session is required");
        if (!AgentTracker.TryPhaseOf(request.Event, out _)) errors.Add("event is not one Claude Code sends (SessionStart, UserPromptSubmit, PreToolUse, PostToolUse, Notification, PermissionRequest, Stop, SessionEnd)");

        return errors.Count > 0 ? (null, errors) : (new AgentEvent(request.Session, request.Project ?? "", request.Event, request.Tool), errors);
    }

    private static string? Clip(string? text)
    {
        string t = (text ?? "").Trim();
        return t.Length == 0 ? null : new string(t.Where(c => !char.IsControl(c)).ToArray());
    }
}

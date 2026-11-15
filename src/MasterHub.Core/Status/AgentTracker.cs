using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Status;

/// <summary>Where a Claude Code session is, as far as its hooks told us.</summary>
public enum AgentPhase
{
    /// <summary>The session started and waits for a first prompt.</summary>
    Ready = 0,
    Working = 1,
    /// <summary>It asks for permission or needs the user: the deck pulses.</summary>
    Permission = 2,
    /// <summary>It finished its turn: it is the user's turn.</summary>
    YourTurn = 3
}

/// <summary>
/// What a Claude Code hook may tell the deck. <b>Only these four things travel</b> – the session id, the project folder name,
/// the event and, for a permission, the tool name – never a prompt, a tool input or a message.
/// </summary>
public sealed record AgentEvent(string SessionId, string Project, string Event, string? Tool);

public sealed record AgentSession(string Id, string Project, AgentPhase Phase, string? Tool, DateTime UpdatedAt);

public interface IAgentTracker
{
    IReadOnlyList<AgentSession> Sessions { get; }

    /// <summary>Applies one hook event. <c>false</c> when the event is unknown or has no session id (nothing changed).</summary>
    bool Apply(AgentEvent e);
}

/// <summary>
/// Keeps the Claude Code sessions and publishes them to the <see cref="IStatusBoard"/> as source <see cref="Source"/>.
/// A session not heard from for <see cref="Silence"/> is dropped (a closed terminal sends no SessionEnd).
/// </summary>
public sealed class AgentTracker : IAgentTracker
{
    public const string Source = "claude";
    public const int MaxSessions = 32;
    public const int MaxText = 64;

    /// <summary>A session that did not send anything for this long is gone.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromHours(6);

    private readonly IStatusBoard _board;
    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, AgentSession> _sessions = new();

    public AgentTracker(IStatusBoard board, Func<DateTime>? clock = null)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<AgentSession> Sessions
    {
        get
        {
            lock (_lock) return _sessions.Values.OrderBy(s => s.Project, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>The hook event names of Claude Code and what they mean. <c>null</c> = the session ended.</summary>
    public static bool TryPhaseOf(string? hookEvent, out AgentPhase? phase)
    {
        phase = null;
        switch ((hookEvent ?? "").Trim().ToLowerInvariant())
        {
            case "sessionstart": phase = AgentPhase.Ready; return true;
            case "userpromptsubmit":
            case "pretooluse":
            case "posttooluse": phase = AgentPhase.Working; return true;
            case "notification":
            case "permissionrequest": phase = AgentPhase.Permission; return true;
            case "stop":
            case "subagentstop": phase = AgentPhase.YourTurn; return true;
            case "sessionend": return true;   // phase stays null: the session is removed
            default: return false;
        }
    }

    public bool Apply(AgentEvent e)
    {
        string id = Clean(e.SessionId);
        if (id.Length == 0 || !TryPhaseOf(e.Event, out var phase)) return false;

        lock (_lock)
        {
            DropSilentLocked();

            if (phase == null)
            {
                _sessions.Remove(id);
            }
            else
            {
                // the folder name only: a full path would tell more about the user than the key can show
                string project = FolderName(e.Project);
                if (project.Length == 0 && _sessions.TryGetValue(id, out var old)) project = old.Project;

                _sessions[id] = new AgentSession(id, project, phase.Value, phase == AgentPhase.Permission ? CleanOrNull(e.Tool) : null, _clock());
                while (_sessions.Count > MaxSessions) _sessions.Remove(_sessions.OrderBy(s => s.Value.UpdatedAt).First().Key);
            }

            Publish();
        }

        return true;
    }

    private void DropSilentLocked()
    {
        var now = _clock();
        foreach (var id in _sessions.Where(s => now - s.Value.UpdatedAt > Silence).Select(s => s.Key).ToList()) _sessions.Remove(id);
    }

    private void Publish()
    {
        var items = _sessions.Values.Select(s => new StatusItem(Source, s.Id, StateOf(s.Phase), s.Project, s.Tool, null, s.UpdatedAt)).ToList();
        _board.Replace(Source, items, Silence);
    }

    public static AppState StateOf(AgentPhase phase) => phase switch
    {
        AgentPhase.Permission => AppState.Waiting,
        AgentPhase.Working => AppState.Working,
        AgentPhase.YourTurn => AppState.Done,
        _ => AppState.Idle
    };

    /// <summary>The last segment of a path (either slash), shortened. Empty input stays empty.</summary>
    public static string FolderName(string? path)
    {
        string trimmed = (path ?? "").Trim().TrimEnd('\\', '/');
        int cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return Clean(cut >= 0 ? trimmed[(cut + 1)..] : trimmed);
    }

    private static string Clean(string? text)
    {
        string t = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return t.Length > MaxText ? t[..MaxText] : t;
    }

    private static string? CleanOrNull(string? text)
    {
        string t = Clean(text);
        return t.Length == 0 ? null : t;
    }
}

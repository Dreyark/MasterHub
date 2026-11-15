using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Actions;

/// <summary>
/// The steps of a multi-action key (T-17 j). Written as text in the key's parameter –
/// <c>Hotkey|Ctrl+C;delay:200;OpenApplication|notepad.exe</c> – or as the key's step list. A step without an action name
/// (<c>calc.exe</c>) opens that program. Each pause is capped at <see cref="MaxStepDelayMs"/>; the runner waits with
/// <c>Task.Delay</c>, never by blocking a thread. Multi-actions inside a multi-action and pauses as actions are not allowed
/// (no loops).
/// </summary>
public static class MultiActionPlan
{
    public const int MaxStepDelayMs = 10_000;
    public const int MaxSteps = 50;

    public static IReadOnlyList<MultiActionStepModel> Parse(string? parameter, IEnumerable<MultiActionStepModel>? model = null)
    {
        var steps = new List<MultiActionStepModel>();
        if (model != null)
        {
            foreach (var s in model)
            {
                if (s == null || !Allowed(s.Action)) continue;
                steps.Add(new MultiActionStepModel { Action = s.Action, Parameter = s.Parameter ?? "", DelayMs = ClampDelay(s.DelayMs) });
            }
        }
        if (steps.Count == 0 && !string.IsNullOrWhiteSpace(parameter))
        {
            foreach (string raw in parameter.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;

                if (part.StartsWith("delay:", StringComparison.OrdinalIgnoreCase) || part.StartsWith("wait:", StringComparison.OrdinalIgnoreCase))
                {
                    int ms = int.TryParse(part[(part.IndexOf(':') + 1)..].Trim().TrimEnd('m', 's', 'M', 'S'), out int d) ? d : 0;
                    if (steps.Count > 0) steps[^1].DelayMs = ClampDelay(steps[^1].DelayMs + ms);
                    continue;
                }

                int bar = part.IndexOf('|');
                string head = bar >= 0 ? part[..bar].Trim() : part;
                string tail = bar >= 0 ? part[(bar + 1)..].Trim() : "";
                if (TryAction(head, out var action))
                {
                    if (Allowed(action)) steps.Add(new MultiActionStepModel { Action = action, Parameter = tail });
                }
                else
                {
                    steps.Add(new MultiActionStepModel { Action = DeckActionType.OpenApplication, Parameter = part });   // "calc.exe"
                }
            }
        }
        return steps.Take(MaxSteps).ToList();
    }

    private static bool Allowed(DeckActionType action) =>
        action is not (DeckActionType.None or DeckActionType.MultiAction or DeckActionType.Delay);

    private static bool TryAction(string text, out DeckActionType action)
    {
        action = DeckActionType.None;
        if (string.IsNullOrWhiteSpace(text) || text.Contains('.') || text.Contains('\\') || text.Contains('/')) return false;
        return Enum.TryParse(text, ignoreCase: true, out action) && Enum.IsDefined(action) && !int.TryParse(text, out _);
    }

    public static int ClampDelay(int ms) => Math.Clamp(ms, 0, MaxStepDelayMs);
}

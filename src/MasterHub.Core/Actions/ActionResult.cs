namespace MasterHub.Core.Actions;

/// <summary>What happened when a key's action ran (T-07).</summary>
public enum ActionOutcome
{
    /// <summary>It worked.</summary>
    Ok,
    /// <summary>It did not work (service offline, rejected, exception). The key shows a red corner until a press succeeds.</summary>
    Failed,
    /// <summary>Nothing to report (the action has no observable result).</summary>
    NotApplicable
}

/// <summary>Result of an action with the reason when it failed.</summary>
public readonly record struct ActionResult(ActionOutcome Outcome, string? Reason = null)
{
    public static ActionResult Ok { get; } = new(ActionOutcome.Ok);
    public static ActionResult NotApplicable { get; } = new(ActionOutcome.NotApplicable);

    public static ActionResult Failed(string reason) => new(ActionOutcome.Failed, reason);

    /// <summary>Maps the <c>bool</c> many services return ("did it work").</summary>
    public static ActionResult FromBool(bool success, string failureReason) => success ? Ok : Failed(failureReason);

    public bool IsFailure => Outcome == ActionOutcome.Failed;
}

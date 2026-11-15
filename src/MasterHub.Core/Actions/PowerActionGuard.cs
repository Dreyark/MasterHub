using System;

namespace MasterHub.Core.Actions;

/// <summary>
/// Policy for destructive power keys (ROADMAP B-02): shutdown, restart and sleep must never fire on a single accidental
/// press. A hardware key needs a deliberate hold; a click in the Studio asks for confirmation instead.
/// </summary>
public static class PowerActionGuard
{
    /// <summary>How long a hardware key has to be held before a power action runs.</summary>
    public const int HoldToConfirmMs = 1500;

    /// <summary>Whether <paramref name="action"/> needs hold-to-confirm / confirmation.</summary>
    public static bool IsDestructive(DeckActionType action) =>
        action is DeckActionType.SystemShutdown or DeckActionType.SystemRestart or DeckActionType.SystemSleep;

    /// <summary>
    /// The press mode and hold time the key engine must use for a key. Destructive keys are forced to
    /// <see cref="KeyPressMode.OnHold"/> with <see cref="HoldToConfirmMs"/> unless the key opted out
    /// (<paramref name="skipConfirmation"/>); every other key keeps its own mode and the engine's default hold time.
    /// </summary>
    public static (KeyPressMode Mode, int? HoldMs) Resolve(DeckActionType action, KeyPressMode configured, bool skipConfirmation)
    {
        if (IsDestructive(action) && !skipConfirmation)
        {
            return (KeyPressMode.OnHold, HoldToConfirmMs);
        }

        return (configured, null);
    }
}

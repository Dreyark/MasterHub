using System;

namespace MasterHub.Core.Devices;

/// <summary>How a key changes the brightness of the deck's screens (T-17).</summary>
public enum BrightnessCommand
{
    Up = 0,
    Down = 1,
    /// <summary>Steps down through the levels and starts again at the top.</summary>
    Cycle = 2
}

/// <summary>
/// The arithmetic of the brightness actions. The keypad's backlight is never taken below <see cref="Min"/>: 0 switches the
/// screen's controller off and can lock it up (see the hardware notes), so a key can dim the deck but never black it out.
/// </summary>
public static class DeckBrightness
{
    public const int Min = 1;
    public const int Max = 100;
    public const int DefaultStep = 10;

    /// <summary>The levels a cycling key goes through, brightest first.</summary>
    public static readonly int[] CycleLevels = { 100, 75, 50, 25, 10 };

    /// <summary>The step from a key's parameter (a percentage); anything unusable is the default step.</summary>
    public static int ParseStep(string? parameter) =>
        int.TryParse((parameter ?? "").Trim().TrimEnd('%'), out int step) && step is >= 1 and <= 50 ? step : DefaultStep;

    /// <param name="current">The brightness now, 0–100.</param>
    /// <param name="step">Percent for up and down.</param>
    /// <returns>The new brightness, always within <see cref="Min"/>–<see cref="Max"/>.</returns>
    public static int Next(int current, BrightnessCommand command, int step = DefaultStep)
    {
        current = Math.Clamp(current, Min, Max);
        step = Math.Clamp(step, 1, 50);

        switch (command)
        {
            case BrightnessCommand.Up:
                return Math.Min(Max, current + step);
            case BrightnessCommand.Down:
                return Math.Max(Min, current - step);
            default:
                // the next level below the current brightness; below the lowest (or equal to it) it goes back to the top
                foreach (int level in CycleLevels)
                    if (level < current) return level;
                return CycleLevels[0];
        }
    }
}

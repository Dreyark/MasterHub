using System;
using MasterHub.Core.Models;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Pomocnik matematycznego mapowania indeksow klawiszy oraz wymiarow siatki
/// z uwzglednieniem fizycznego obrotu klawiatury LCD 15 (MHKD15).
/// </summary>
public static class KeyOrientationHelper
{
    /// <summary>
    /// Mapuje logiczny indeks kafelka (0..14 w zaleznosci od orientacji) na fizyczny numer klawisza sprzetowego (0..14).
    /// </summary>
    public static int LogicalToPhysicalIndex(int logicalIndex, int angleDegrees)
    {
        if (logicalIndex < 0 || logicalIndex >= 15) return logicalIndex;

        int normAngle = NormalizeAngle(angleDegrees);
        switch (normAngle)
        {
            case 90:
            {
                // Siatka 3 kolumny x 5 wierszy (Portrait)
                int r = logicalIndex / 3;
                int c = logicalIndex % 3;
                int origR = 2 - c;
                int origC = r;
                return Math.Clamp(origR * 5 + origC, 0, 14);
            }
            case 180:
            {
                // Odwrocona siatka 5x3
                return 14 - logicalIndex;
            }
            case 270:
            {
                // Siatka 3 kolumny x 5 wierszy (Odwrocony Portrait)
                int r = logicalIndex / 3;
                int c = logicalIndex % 3;
                int origR = c;
                int origC = 4 - r;
                return Math.Clamp(origR * 5 + origC, 0, 14);
            }
            default:
                // 0° (Normal Landscape 5x3)
                return logicalIndex;
        }
    }

    /// <summary>
    /// Mapuje fizyczny numer wcisnietego klawisza (0..14) na logiczny indeks kafelka (0..14) w biezacej orientacji.
    /// </summary>
    public static int PhysicalToLogicalIndex(int physicalIndex, int angleDegrees)
    {
        if (physicalIndex < 0 || physicalIndex >= 15) return physicalIndex;

        int normAngle = NormalizeAngle(angleDegrees);
        switch (normAngle)
        {
            case 90:
            {
                int origR = physicalIndex / 5;
                int origC = physicalIndex % 5;
                int r = origC;
                int c = 2 - origR;
                return Math.Clamp(r * 3 + c, 0, 14);
            }
            case 180:
            {
                return 14 - physicalIndex;
            }
            case 270:
            {
                int origR = physicalIndex / 5;
                int origC = physicalIndex % 5;
                int r = 4 - origC;
                int c = origR;
                return Math.Clamp(r * 3 + c, 0, 14);
            }
            default:
                return physicalIndex;
        }
    }

    /// <summary>
    /// Zwraca wymiary siatki UI (liczba kolumn i wierszy) dla zadanego kata obrotu.
    /// </summary>
    public static (int columns, int rows) GetGridDimensions(int angleDegrees)
    {
        int normAngle = NormalizeAngle(angleDegrees);
        if (normAngle == 90 || normAngle == 270)
        {
            return (columns: 3, rows: 5);
        }
        return (columns: 5, rows: 3);
    }

    public static int NormalizeAngle(int angleDegrees)
    {
        int mod = angleDegrees % 360;
        if (mod < 0) mod += 360;
        if (mod >= 315 || mod < 45) return 0;
        if (mod >= 45 && mod < 135) return 90;
        if (mod >= 135 && mod < 225) return 180;
        return 270;
    }
}

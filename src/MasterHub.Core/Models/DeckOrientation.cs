namespace MasterHub.Core.Models;

/// <summary>
/// Fizyczna orientacja podstawki MasterHub (MHBS01) raportowana przez sprzetowy akcelerometr.
/// </summary>
public enum DeckOrientation
{
    Horizontal = 0, // 0° Pozioma (Landscape, domyslna)
    Vertical = 1    // 90° Pionowa (Portrait)
}

/// <summary>
/// Efektywny kat obrotu siatki i grafiki klawiszy LCD.
/// </summary>
public enum DeckRotationAngle
{
    Deg0 = 0,
    Deg90 = 90,
    Deg180 = 180,
    Deg270 = 270
}

/// <summary>
/// Tryb rotacji modulow w aplikacji.
/// </summary>
public enum DeckRotationMode
{
    Auto = 0,     // Automatycznie zalezne od sprzetowego akcelerometru stacji bazowej
    Manual0 = 1,  // Wymuszone 0° (Pozioma / 5x3)
    Manual90 = 2, // Wymuszone 90° (Pionowa / 3x5)
    Manual180 = 3,// Wymuszone 180° (Odwrocona pozioma / 5x3)
    Manual270 = 4 // Wymuszone 270° (Odwrocona pionowa / 3x5)
}

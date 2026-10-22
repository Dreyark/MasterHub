using System;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Serwis odpowiedzialny za automatyczną ekstrakcję ikon wysokiej rozdzielczości (128x128 / 256x256)
/// z plików wykonywalnych (.exe), skrótów (.lnk, .url), plików .ico oraz gier platformy Steam.
/// Skaluje ikony do wskazanego rozmiaru (domyślnie 72x72) z antyaliasingiem i zachowaniem przezroczystości.
/// </summary>
public interface IAppIconExtractorService
{
    /// <summary>
    /// Wyciąga ikonę z pliku, skrótu lub URL gry Steam, skaluje do 72x72 i zapisuje w folderze pamięci podręcznej.
    /// Zwraca bezwzględną ścieżkę do wygenerowanego pliku PNG lub null w przypadku niepowodzenia.
    /// </summary>
    string? ExtractAndSaveIcon(string targetPathOrUrl, int targetSize = 72);

    /// <summary>
    /// Wyciąga ikonę i zwraca skompresowane bajty PNG o wskazanym rozmiarze z zachowaniem proporcji i przezroczystości.
    /// </summary>
    byte[]? ExtractIconPngBytes(string targetPathOrUrl, int targetSize = 72);

    /// <summary>
    /// Wyciąga ikonę jako obiekt SKBitmap ze SkiaSharp o wskazanym rozmiarze.
    /// </summary>
    SKBitmap? ExtractIconBitmap(string targetPathOrUrl, int targetSize = 72);

    /// <summary>
    /// Sprawdza, czy podany ciąg znaków to identyfikator / URL gry Steam (np. steam://rungameid/730).
    /// </summary>
    bool IsSteamUrl(string url);

    /// <summary>
    /// Wyciąga numeryczny identyfikator Steam AppID z podanego URL lub ciągu tekstowego.
    /// </summary>
    string? ExtractSteamAppId(string url);

    /// <summary>
    /// Rozwiązuje lokalną ścieżkę do pliku wykonywalnego gry Steam lub jej pliku .ico na podstawie AppID.
    /// </summary>
    string? ResolveSteamGameTarget(string appId);

    /// <summary>
    /// Rozwiązuje ścieżkę do pliku docelowego ze skrótu .lnk lub .url.
    /// </summary>
    string? ResolveShortcutTarget(string shortcutPath);
}

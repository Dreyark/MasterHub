using System;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Interfejs dedykowanego silnika renderowania i wysyłania grafik na fizyczne klawisze LCD (Etap 3.1).
/// </summary>
public interface IKeyRenderer
{
    /// <summary>
    /// Renderuje i wysyła grafikę z pliku dyskowego na wskazany klawisz LCD (0-14).
    /// </summary>
    void SetKeyImage(int keyIndex, string imagePath);

    /// <summary>
    /// Renderuje i wysyła bitmapę SkiaSharp na wskazany klawisz LCD (0-14).
    /// </summary>
    void SetKeyImageFromBitmap(int keyIndex, SKBitmap bmp);

    /// <summary>
    /// Renderuje i wysyła sformatowany tekst z kolorem tła i czcionki na wskazany klawisz LCD (0-14).
    /// </summary>
    void SetKeyText(int keyIndex, string text, SKColor bg, SKColor fg);

    /// <summary>
    /// Wysyła gotowe dane JPEG na wskazany klawisz LCD (0-14).
    /// </summary>
    void SetKeyImageFromJpeg(int keyIndex, byte[] jpeg);

    /// <summary>
    /// Czyści wyświetlacz wskazanego klawisza LCD do koloru czarnego.
    /// </summary>
    void ClearKey(int keyIndex);

    /// <summary>
    /// Zdarzenie informujące o wyrenderowaniu i wysłaniu nowej ramki obrazu na dany klawisz.
    /// </summary>
    event Action<int, byte[]>? KeyRendered;
}

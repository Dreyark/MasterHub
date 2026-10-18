using System;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Interfejs silnika odtwarzania animowanych ikon GIF na klawiszach LCD (Etap 3.4).
/// </summary>
public interface IAnimatedKeyRenderer : IDisposable
{
    /// <summary>
    /// Uruchamia odtwarzanie animacji GIF na wskazanym klawiszu LCD (0-14).
    /// </summary>
    void PlayGif(int keyIndex, string gifPath, int fps = 10);

    /// <summary>
    /// Uruchamia odtwarzanie multimediów (GIF, WebP, wideo MP4/AVI/MOV/WMV/WEBM/MKV) na wskazanym klawiszu LCD (0-14).
    /// </summary>
    void PlayMedia(int keyIndex, string mediaPath, int fps = 10);

    /// <summary>
    /// Uruchamia odtwarzanie multimediów na wskazanym klawiszu LCD (0-14) z uwzględnieniem kadrowania (fit, zoom, pan).
    /// </summary>
    void PlayMedia(int keyIndex, string mediaPath, int fps, string fitMode, int scale, int offsetX, int offsetY);

    /// <summary>
    /// Uruchamia odtwarzanie animacji z listy pre-renderowanych klatek JPEG (0-14).
    /// </summary>
    void PlayFrames(int keyIndex, System.Collections.Generic.IReadOnlyList<byte[]> frames, int fps = 10);

    /// <summary>
    /// Zatrzymuje animację na wskazanym klawiszu LCD.
    /// </summary>
    void StopAnimation(int keyIndex);

    /// <summary>
    /// Zatrzymuje wszystkie aktywne animacje klawiszy.
    /// </summary>
    void StopAllAnimations();

    /// <summary>
    /// Sprawdza, czy na danym klawiszu trwa aktywne odtwarzanie animacji.
    /// </summary>
    bool IsAnimating(int keyIndex);

    /// <summary>
    /// Zwraca liczbę wyekstrahowanych klatek animacji dla danego klawisza.
    /// </summary>
    int GetFrameCount(int keyIndex);

    /// <summary>
    /// Zdarzenie informujące o wyrenderowaniu klatki (klawisz, numer klatki).
    /// </summary>
    event Action<int, int>? FrameRendered;

    /// <summary>
    /// Zdarzenie informujące o wyrenderowaniu klatki z gotowym buforem JPEG (klawisz, numer klatki, bajty JPEG).
    /// </summary>
    event Action<int, int, byte[]>? FrameRenderedWithJpeg;
}

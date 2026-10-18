using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MasterHub.Core.Media;

/// <summary>
/// Serwis wykrywający aktualnie odtwarzany utwór muzyczny (Now Playing) na platformie Windows.
/// Automatycznie odczytuje stan aplikacji multimedialnych (Spotify, YouTube, VLC, odtwarzacze systemowe)
/// bez konieczności konfigurowania kluczy API.
/// </summary>
public sealed class NowPlayingService : INowPlayingService
{
    private readonly object _syncRoot = new();
    private Timer? _timer;
    private bool _isEnabled = true;
    private bool _disposed;

    private string _currentTitle = "";
    private string _currentArtist = "";
    private string _currentAlbum = "";
    private bool _isPlaying;

    private MediaPlaybackStatus _playbackStatus = MediaPlaybackStatus.Stopped;
    private byte[]? _currentArtworkJpeg;

    public bool IsEnabled
    {
        get
        {
            lock (_syncRoot) return _isEnabled;
        }
        set
        {
            lock (_syncRoot)
            {
                _isEnabled = value;
                if (!value)
                {
                    _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
                else
                {
                    _timer?.Change(0, 1000);
                }
            }
        }
    }

    public string CurrentTitle { get { lock (_syncRoot) return _currentTitle; } }
    public string CurrentArtist { get { lock (_syncRoot) return _currentArtist; } }
    public string CurrentAlbum { get { lock (_syncRoot) return _currentAlbum; } }
    public bool IsPlaying { get { lock (_syncRoot) return _isPlaying; } }
    public MediaPlaybackStatus PlaybackStatus { get { lock (_syncRoot) return _playbackStatus; } }
    public byte[]? CurrentArtworkJpeg { get { lock (_syncRoot) return _currentArtworkJpeg; } }

    public NowPlayingInfo CurrentTrackInfo
    {
        get
        {
            lock (_syncRoot)
            {
                return new NowPlayingInfo
                {
                    Title = _currentTitle,
                    Artist = _currentArtist,
                    Album = _currentAlbum,
                    PlaybackStatus = _playbackStatus,
                    ArtworkJpeg = _currentArtworkJpeg
                };
            }
        }
    }

    public string DisplayTrack
    {
        get
        {
            lock (_syncRoot)
            {
                if (!_isPlaying || (string.IsNullOrWhiteSpace(_currentTitle) && string.IsNullOrWhiteSpace(_currentArtist)))
                {
                    return "Brak odtwarzania";
                }

                if (!string.IsNullOrWhiteSpace(_currentArtist) && !string.IsNullOrWhiteSpace(_currentTitle))
                {
                    return $"{_currentArtist} - {_currentTitle}";
                }

                return !string.IsNullOrWhiteSpace(_currentTitle) ? _currentTitle : _currentArtist;
            }
        }
    }

    public event Action? TrackChanged;
    public event Action<NowPlayingInfo>? TrackInfoChanged;

    public NowPlayingService()
    {
        _timer = new Timer(OnTimerTick, null, 200, 1000);
    }

    public void CheckNow()
    {
        EvaluateMediaState();
    }

    public void SetTrackInfo(string artist, string title, string album = "", bool isPlaying = true, MediaPlaybackStatus status = MediaPlaybackStatus.Playing, byte[]? artworkJpeg = null)
    {
        bool changed = false;
        NowPlayingInfo? info = null;
        lock (_syncRoot)
        {
            MediaPlaybackStatus effectiveStatus = !isPlaying ? MediaPlaybackStatus.Stopped : status;
            if (_currentTitle != title ||
                _currentArtist != artist ||
                _currentAlbum != album ||
                _isPlaying != isPlaying ||
                _playbackStatus != effectiveStatus ||
                _currentArtworkJpeg != artworkJpeg)
            {
                _currentTitle = title ?? "";
                _currentArtist = artist ?? "";
                _currentAlbum = album ?? "";
                _isPlaying = isPlaying;
                _playbackStatus = effectiveStatus;
                _currentArtworkJpeg = artworkJpeg;
                changed = true;
            }

            info = new NowPlayingInfo
            {
                Title = _currentTitle,
                Artist = _currentArtist,
                Album = _currentAlbum,
                PlaybackStatus = _playbackStatus,
                ArtworkJpeg = _currentArtworkJpeg
            };
        }

        if (changed && info != null)
        {
            TrackChanged?.Invoke();
            TrackInfoChanged?.Invoke(info);
        }
    }

    private void OnTimerTick(object? state)
    {
        lock (_syncRoot)
        {
            if (!_isEnabled || _disposed) return;
        }

        EvaluateMediaState();
    }

    private void EvaluateMediaState()
    {
        try
        {
            string detectedTitle = "";
            string detectedArtist = "";
            string detectedSource = "";
            bool isAudioActive = false;

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len < 3) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                // 1. Spotify
                if (title.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                {
                    if (!title.Equals("Spotify", StringComparison.OrdinalIgnoreCase) &&
                        !title.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase) &&
                        !title.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase))
                    {
                        int sep = title.IndexOf(" - ", StringComparison.Ordinal);
                        if (sep > 0)
                        {
                            detectedArtist = title.Substring(0, sep).Trim();
                            detectedTitle = title.Substring(sep + 3).Trim();
                        }
                        else
                        {
                            detectedTitle = title.Trim();
                            detectedArtist = "Spotify";
                        }
                        detectedSource = "Spotify";
                        isAudioActive = true;
                        return false;
                    }
                }

                // 2. VLC
                if (title.Contains("VLC media player", StringComparison.OrdinalIgnoreCase))
                {
                    string clean = title.Replace(" - VLC media player", "").Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        detectedTitle = clean;
                        detectedArtist = "VLC";
                        detectedSource = "VLC Media Player";
                        isAudioActive = true;
                        return false;
                    }
                }

                // 3. Przeglądarki (YouTube / YouTube Music)
                if (title.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("- YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    string clean = title.Replace(" - YouTube Music", "").Replace("- YouTube", "").Trim();
                    int sep = clean.IndexOf(" - ", StringComparison.Ordinal);
                    if (sep > 0)
                    {
                        detectedArtist = clean.Substring(0, sep).Trim();
                        detectedTitle = clean.Substring(sep + 3).Trim();
                    }
                    else
                    {
                        detectedTitle = clean;
                        detectedArtist = "YouTube";
                    }
                    detectedSource = "Przeglądarka";
                    isAudioActive = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            bool changed = false;
            NowPlayingInfo? info = null;
            lock (_syncRoot)
            {
                var newStatus = isAudioActive ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Stopped;
                if (_currentTitle != detectedTitle ||
                    _currentArtist != detectedArtist ||
                    _currentAlbum != detectedSource ||
                    _isPlaying != isAudioActive ||
                    _playbackStatus != newStatus)
                {
                    _currentTitle = detectedTitle;
                    _currentArtist = detectedArtist;
                    _currentAlbum = detectedSource;
                    _isPlaying = isAudioActive;
                    _playbackStatus = newStatus;
                    changed = true;
                }

                info = new NowPlayingInfo
                {
                    Title = _currentTitle,
                    Artist = _currentArtist,
                    Album = _currentAlbum,
                    PlaybackStatus = _playbackStatus,
                    ArtworkJpeg = _currentArtworkJpeg
                };
            }

            if (changed && info != null)
            {
                TrackChanged?.Invoke();
                TrackInfoChanged?.Invoke(info);
            }
        }
        catch { }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _timer?.Dispose();
                _timer = null;
            }
            _disposed = true;
        }
    }
}

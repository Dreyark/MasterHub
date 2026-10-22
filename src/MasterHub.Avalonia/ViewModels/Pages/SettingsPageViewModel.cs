using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.SystemControl;

namespace MasterHub.Avalonia.ViewModels.Pages;

public partial class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly IMasterHubDeviceService _deviceService;
    private readonly CanvasViewModel _canvas;
    private readonly IDiscordService _discordService;
    private readonly IObsService _obsService;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IProfileService _profileService;
    private readonly ISystemControlService _systemControl;
    private readonly IStartupService _startupService;

    public CanvasViewModel Canvas => _canvas;

    public IReadOnlyList<MasterHub.Core.Localization.LanguageOption> AvailableLanguages => LocalizationService.Instance.SupportedLanguages;

    [ObservableProperty]
    private MasterHub.Core.Localization.LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimizedToTray = true;

    [ObservableProperty]
    private string _discordClientId = "207646673902501888";

    [ObservableProperty]
    private string _discordClientSecret = "";

    [ObservableProperty]
    private string _discordAccessToken = "";

    [ObservableProperty]
    private string _discordMuteHotkey = "Ctrl+Shift+M";

    [ObservableProperty]
    private string _discordDeafenHotkey = "Ctrl+Shift+D";

    [ObservableProperty]
    private bool _discordUseRpc = false;

    [ObservableProperty]
    private string _discordStatusMessage = "";

    public bool IsDiscordConnected => _discordService.IsConnected;
    public bool IsDiscordAuthenticated => _discordService.IsAuthenticated;
    public string DiscordUsername => _discordService.CurrentUsername ?? "";
    public string DiscordLoggedInInfoDisplay => string.Format(
        LocalizationService.Instance.GetString("Discord_LoggedInAs", "Zalogowano jako: {0}. Wyciszanie działa w 100% bezpośrednio przez API."),
        DiscordUsername);

    // --- OBS STUDIO ---
    [ObservableProperty]
    private string _obsHost = "localhost";

    [ObservableProperty]
    private int _obsPort = 4455;

    [ObservableProperty]
    private string _obsPassword = "";

    [ObservableProperty]
    private bool _obsAutoConnect = true;

    [ObservableProperty]
    private string _obsStatusMessage = "";

    public bool IsObsConnected => _obsService.IsConnected;
    public string? ObsVersion => _obsService.ObsWebSocketVersion;

    public string ObsConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_obsService.IsConnected) return loc.GetString("Obs_Status_Disconnected", "ROZŁĄCZONO (OBS Studio offline)");
            if (!string.IsNullOrEmpty(_obsService.ObsWebSocketVersion))
                return string.Format(loc.GetString("Obs_Status_ConnectedWithVer", "POŁĄCZONO (OBS v5: {0})"), _obsService.ObsWebSocketVersion);
            return loc.GetString("Obs_Status_Connected", "POŁĄCZONO (OBS Studio WebSocket v5)");
        }
    }

    public string ObsConnectionStatusColor => _obsService.IsConnected ? "#10B981" : "#EF4444";

    public string ObsStatusMessageColor
    {
        get
        {
            if (IsObsConnected) return "#10B981";
            if (ObsStatusMessage.Contains("Błąd", StringComparison.OrdinalIgnoreCase) ||
                ObsStatusMessage.Contains("nie udało", StringComparison.OrdinalIgnoreCase) ||
                ObsStatusMessage.Contains("wymaga", StringComparison.OrdinalIgnoreCase) ||
                ObsStatusMessage.Contains("odrzucił", StringComparison.OrdinalIgnoreCase) ||
                ObsStatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                ObsStatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }
            if (ObsStatusMessage.Contains("Łączenie", StringComparison.OrdinalIgnoreCase) ||
                ObsStatusMessage.Contains("Connecting", StringComparison.OrdinalIgnoreCase))
            {
                return "#38BDF8";
            }
            return "#94A3B8";
        }
    }

    public string DiscordConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_discordService.IsConnected) return loc.GetString("Discord_Status_Disconnected", "ROZŁĄCZONO (Uruchom Discord Desktop)");
            if (_discordService.IsAuthenticated) return string.Format(loc.GetString("Discord_Status_ConnectedRpc", "POŁĄCZONO ({0}) — RPC AKTYWNE"), _discordService.CurrentUsername);
            if (DiscordUseRpc) return loc.GetString("Discord_Status_AuthRequired", "WYKRYTO DISCORD — WYMAGANA AUTORYZACJA");
            return string.Format(loc.GetString("Discord_Status_DetectedShortcuts", "WYKRYTO ({0}) — TRYB SKRÓTÓW"), _discordService.CurrentUsername ?? "Discord Desktop");
        }
    }

    public string DiscordConnectionStatusColor
    {
        get
        {
            if (!_discordService.IsConnected) return "#EF4444";
            if (_discordService.IsAuthenticated) return "#10B981";
            return "#F59E0B";
        }
    }

    public bool ShowRpcAuthenticatedState => DiscordUseRpc && IsDiscordAuthenticated;
    public bool ShowRpcUnauthenticatedState => DiscordUseRpc && !IsDiscordAuthenticated;

    // --- TWITCH ---
    [ObservableProperty]
    private string _twitchChannel = "";

    [ObservableProperty]
    private string _twitchBotUsername = "";

    [ObservableProperty]
    private string _twitchOAuthToken = "";

    [ObservableProperty]
    private string _twitchClientId = "";

    [ObservableProperty]
    private bool _twitchAutoConnect = true;

    [ObservableProperty]
    private string _twitchStatusMessage = "";

    public bool IsTwitchConnected => _twitchService.IsConnected;

    public string TwitchConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_twitchService.IsConnected) return loc.GetString("Twitch_Status_Disconnected", "ROZŁĄCZONO (Brak połączenia z Twitch)");
            return string.Format(loc.GetString("Twitch_Status_Connected", "POŁĄCZONO (#{0})"), _twitchService.ChannelName ?? TwitchChannel);
        }
    }

    public string TwitchConnectionStatusColor => _twitchService.IsConnected ? "#10B981" : "#EF4444";

    public string TwitchStatusMessageColor
    {
        get
        {
            if (IsTwitchConnected) return "#10B981";
            if (TwitchStatusMessage.Contains("Błąd", StringComparison.OrdinalIgnoreCase) ||
                TwitchStatusMessage.Contains("nie udało", StringComparison.OrdinalIgnoreCase) ||
                TwitchStatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                TwitchStatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }
            if (TwitchStatusMessage.Contains("Łączenie", StringComparison.OrdinalIgnoreCase) ||
                TwitchStatusMessage.Contains("Connecting", StringComparison.OrdinalIgnoreCase))
            {
                return "#9146FF";
            }
            return "#94A3B8";
        }
    }

    // --- KICK ---
    [ObservableProperty]
    private string _kickChannel = "";

    [ObservableProperty]
    private string _kickBotUsername = "";

    [ObservableProperty]
    private string _kickAuthToken = "";

    [ObservableProperty]
    private bool _kickAutoConnect = true;

    [ObservableProperty]
    private string _kickStatusMessage = "";

    public bool IsKickConnected => _kickService.IsConnected;

    public string KickConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_kickService.IsConnected) return loc.GetString("Kick_Status_Disconnected", "ROZŁĄCZONO (Brak połączenia z Kick)");
            return string.Format(loc.GetString("Kick_Status_Connected", "POŁĄCZONO (kick.com/{0})"), _kickService.ChannelName ?? KickChannel);
        }
    }

    public string KickConnectionStatusColor => _kickService.IsConnected ? "#53FC18" : "#EF4444";

    public string KickStatusMessageColor
    {
        get
        {
            if (IsKickConnected) return "#53FC18";
            if (KickStatusMessage.Contains("Błąd", StringComparison.OrdinalIgnoreCase) ||
                KickStatusMessage.Contains("nie udało", StringComparison.OrdinalIgnoreCase) ||
                KickStatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                KickStatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }
            if (KickStatusMessage.Contains("Łączenie", StringComparison.OrdinalIgnoreCase) ||
                KickStatusMessage.Contains("Connecting", StringComparison.OrdinalIgnoreCase))
            {
                return "#53FC18";
            }
            return "#94A3B8";
        }
    }

    // --- JETKVM (OPTION A: DIRECT LOCAL HTTP/RPC) ---
    private readonly IJetKvmService _jetKvmService;

    [ObservableProperty]
    private string _jetKvmHost = "192.168.1.100";

    [ObservableProperty]
    private int _jetKvmPort = 80;

    [ObservableProperty]
    private bool _jetKvmUseHttps;

    [ObservableProperty]
    private string _jetKvmAuthToken = "";

    [ObservableProperty]
    private bool _jetKvmAutoConnect = true;

    [ObservableProperty]
    private string _jetKvmStatusMessage = "";

    public bool IsJetKvmConnected => _jetKvmService.IsConnected;
    public bool IsJetKvmPowerOn => _jetKvmService.LastPowerState;
    public bool IsJetKvmHddActive => _jetKvmService.LastHddState;

    public string JetKvmConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_jetKvmService.IsConnected) return loc.GetString("JetKvm_Status_Disconnected", "ROZŁĄCZONO (Brak połączenia z JetKVM)");
            if (!string.IsNullOrEmpty(_jetKvmService.DeviceId))
                return string.Format(loc.GetString("JetKvm_Status_ConnectedWithId", "POŁĄCZONO ({0} • ID: {1})"), JetKvmHost, _jetKvmService.DeviceId);
            return string.Format(loc.GetString("JetKvm_Status_Connected", "POŁĄCZONO ({0})"), JetKvmHost);
        }
    }

    public string JetKvmPowerStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_jetKvmService.IsConnected) return loc.GetString("JetKvm_Power_Unknown", "Stan zasilania: NIEZNANY");
            return _jetKvmService.LastPowerState
                ? loc.GetString("JetKvm_Power_On", "Zasilanie: WŁĄCZONE (ON)")
                : loc.GetString("JetKvm_Power_Off", "Zasilanie: WYŁĄCZONE (OFF)");
        }
    }

    public string JetKvmPowerStatusColor => !_jetKvmService.IsConnected ? "#94A3B8" : (_jetKvmService.LastPowerState ? "#10B981" : "#EF4444");

    public string JetKvmConnectionStatusColor => _jetKvmService.IsConnected ? "#10B981" : "#EF4444";

    public string JetKvmStatusMessageColor
    {
        get
        {
            if (JetKvmStatusMessage.Contains("Błąd", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("nie udało", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("Niepoprawne", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("wymaga", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("Brak łączności", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("Nie można", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }
            if (JetKvmStatusMessage.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("Połączono", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("sukces", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("Wysłano", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("zapisane", StringComparison.OrdinalIgnoreCase))
            {
                return "#10B981";
            }
            if (JetKvmStatusMessage.Contains("Sprawdzanie", StringComparison.OrdinalIgnoreCase) ||
                JetKvmStatusMessage.Contains("Łączenie", StringComparison.OrdinalIgnoreCase))
            {
                return "#38BDF8";
            }
            return "#94A3B8";
        }
    }

    // --- SPOTIFY WEB API & PLAYBACK ---
    private readonly ISpotifyService _spotifyService;

    [ObservableProperty]
    private string _spotifyClientId = "";

    [ObservableProperty]
    private string _spotifyClientSecret = "";

    [ObservableProperty]
    private bool _spotifyAutoConnect = true;

    [ObservableProperty]
    private bool _spotifyFallbackToDesktopHotkeys = true;

    [ObservableProperty]
    private string _spotifyStatusMessage = "";

    [ObservableProperty]
    private string _spotifyTestUri = "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M";

    public ObservableCollection<SpotifyPlaylistItem> SpotifyPlaylists { get; } = new();

    public bool IsSpotifyConnected => _spotifyService.IsConnected;

    public string SpotifyConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_spotifyService.IsConnected)
                return loc.GetString("Spotify_Status_Disconnected", "ROZŁĄCZONO (Brak autoryzacji Spotify Web API)");
            return loc.GetString("Spotify_Status_Connected", "POŁĄCZONO (Spotify Web API aktywny)");
        }
    }

    public string SpotifyConnectionStatusColor => _spotifyService.IsConnected ? "#10B981" : "#EF4444";

    public string SpotifyNowPlayingText
    {
        get
        {
            var state = _spotifyService.CurrentState;
            if (string.IsNullOrWhiteSpace(state.TrackTitle))
                return LocalizationService.Instance.GetString("Spotify_NoTrack", "Brak aktywnego odtwarzania");
            return $"{state.TrackTitle} — {state.ArtistName}";
        }
    }

    public string SpotifyShuffleStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _spotifyService.CurrentState.IsShuffle
                ? loc.GetString("Spotify_Shuffle_On", "Losowo: WŁĄCZONE")
                : loc.GetString("Spotify_Shuffle_Off", "Losowo: WYŁĄCZONE");
        }
    }

    public string SpotifyRepeatStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _spotifyService.CurrentState.RepeatMode switch
            {
                SpotifyRepeatMode.Track => loc.GetString("Spotify_Repeat_Track", "Powtarzanie: UTWÓR"),
                SpotifyRepeatMode.Context => loc.GetString("Spotify_Repeat_Context", "Powtarzanie: WSZYSTKO"),
                _ => loc.GetString("Spotify_Repeat_Off", "Powtarzanie: WYŁĄCZONE")
            };
        }
    }

    public string SpotifyLikedStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _spotifyService.CurrentState.IsLiked
                ? loc.GetString("Spotify_Liked_Yes", "Polubione: TAK (w bibliotece)")
                : loc.GetString("Spotify_Liked_No", "Polubione: NIE");
        }
    }

    public string SpotifyStatusMessageColor
    {
        get
        {
            if (SpotifyStatusMessage.Contains("Błąd", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("nie udało", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("Niepoprawne", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("wymaga", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }
            if (SpotifyStatusMessage.Contains("Pomyślnie", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("Połączono", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("sukces", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("zapisane", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("Wysłano", StringComparison.OrdinalIgnoreCase))
            {
                return "#10B981";
            }
            if (SpotifyStatusMessage.Contains("Otwieranie", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("Logowanie", StringComparison.OrdinalIgnoreCase) ||
                SpotifyStatusMessage.Contains("Pobieranie", StringComparison.OrdinalIgnoreCase))
            {
                return "#38BDF8";
            }
            return "#94A3B8";
        }
    }

    // --- VLC MEDIA PLAYER HTTP INTERFACE ---
    private readonly IVlcService _vlcService;

    [ObservableProperty]
    private string _vlcHost = "127.0.0.1";

    [ObservableProperty]
    private int _vlcPort = 8080;

    [ObservableProperty]
    private string _vlcPassword = "";

    [ObservableProperty]
    private bool _vlcAutoConnect = true;

    [ObservableProperty]
    private bool _vlcFallbackToDesktopHotkeys = true;

    [ObservableProperty]
    private string _vlcStatusMessage = "";

    public bool IsVlcConnected => _vlcService.IsConnected;

    public string VlcConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_vlcService.IsConnected)
                return loc.GetString("Vlc_Status_Disconnected", "ROZŁĄCZONO (VLC offline / brak HTTP)");
            return string.Format(loc.GetString("Vlc_Status_Connected", "POŁĄCZONO (VLC {0})"), _vlcService.CurrentState.Version ?? "Lua Web");
        }
    }

    public string VlcConnectionStatusColor => _vlcService.IsConnected ? "#10B981" : "#EF4444";

    public string VlcNowPlayingText
    {
        get
        {
            var state = _vlcService.CurrentState;
            if (string.IsNullOrWhiteSpace(state.Title))
                return LocalizationService.Instance.GetString("Vlc_NoTrack", "Brak aktywnego odtwarzania");
            return !string.IsNullOrWhiteSpace(state.Artist) ? $"{state.Title} — {state.Artist}" : state.Title;
        }
    }

    public string VlcPlaybackStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _vlcService.PlaybackState switch
            {
                VlcPlaybackStateEnum.Playing => loc.GetString("Vlc_State_Playing", "Stan: ODTWARZANIE"),
                VlcPlaybackStateEnum.Paused => loc.GetString("Vlc_State_Paused", "Stan: WSTRZYMANO"),
                _ => loc.GetString("Vlc_State_Stopped", "Stan: ZATRZYMANO")
            };
        }
    }

    public string VlcFullscreenStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _vlcService.IsFullscreen
                ? loc.GetString("Vlc_Fullscreen_On", "Ekran: PEŁNY")
                : loc.GetString("Vlc_Fullscreen_Off", "Ekran: OKNO");
        }
    }

    public string VlcStatusMessageColor
    {
        get
        {
            if (VlcStatusMessage.Contains("Błąd", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("nie udało", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("Niepoprawne", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("Nie można", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }
            if (VlcStatusMessage.Contains("Pomyślnie", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("Połączono", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("sukces", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("zapisane", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("Wysłano", StringComparison.OrdinalIgnoreCase))
            {
                return "#10B981";
            }
            if (VlcStatusMessage.Contains("Łączenie", StringComparison.OrdinalIgnoreCase) ||
                VlcStatusMessage.Contains("Connecting", StringComparison.OrdinalIgnoreCase))
            {
                return "#38BDF8";
            }
            return "#94A3B8";
        }
    }

    // --- VOICEMOD LIVE VOICE CHANGER ---
    private readonly IVoicemodService _voicemodService;

    [ObservableProperty]
    private string _voicemodHost = "127.0.0.1";

    [ObservableProperty]
    private int _voicemodPort = 59129;

    [ObservableProperty]
    private string _voicemodApiKey = "";

    [ObservableProperty]
    private bool _voicemodAutoConnect = true;

    [ObservableProperty]
    private bool _voicemodFallbackToDesktopHotkeys = true;

    [ObservableProperty]
    private string _voicemodStatusMessage = "";

    public static event Action? ScrollToVoicemodRequested;
    public static void RequestScrollToVoicemod() => ScrollToVoicemodRequested?.Invoke();

    public bool IsVoicemodConnected => _voicemodService.IsConnected;

    public string VoicemodConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_voicemodService.IsConnected)
                return loc.GetString("Voicemod_Status_Disconnected", "ROZŁĄCZONO (Voicemod offline / brak WebSocket)");
            return loc.GetString("Voicemod_Status_Connected", "POŁĄCZONO (Voicemod v1 Control API)");
        }
    }

    public string VoicemodConnectionStatusColor => _voicemodService.IsConnected ? "#10B981" : "#EF4444";

    public string VoicemodVoiceChangerStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _voicemodService.CurrentState.IsVoiceChangerEnabled
                ? loc.GetString("Voicemod_VoiceChanger_On", "Modulacja: WŁĄCZONA")
                : loc.GetString("Voicemod_VoiceChanger_Off", "Modulacja: WYŁĄCZONA");
        }
    }

    public string VoicemodHearStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _voicemodService.CurrentState.IsHearMyVoiceEnabled
                ? loc.GetString("Voicemod_Hear_On", "Odsłuch: WŁĄCZONY")
                : loc.GetString("Voicemod_Hear_Off", "Odsłuch: WYŁĄCZONY");
        }
    }

    public string VoicemodMicStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _voicemodService.CurrentState.IsMicMuted
                ? loc.GetString("Voicemod_Mute_On", "Mikrofon: WYCISZONY")
                : loc.GetString("Voicemod_Mute_Off", "Mikrofon: AKTYWNY");
        }
    }

    public string VoicemodBgStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _voicemodService.CurrentState.IsBackgroundEffectsEnabled
                ? loc.GetString("Voicemod_Bg_On", "Tło FX: WŁĄCZONE")
                : loc.GetString("Voicemod_Bg_Off", "Tło FX: WYŁĄCZONE");
        }
    }

    public string VoicemodActiveVoiceText
    {
        get
        {
            var state = _voicemodService.CurrentState;
            if (string.IsNullOrWhiteSpace(state.CurrentVoiceName))
                return LocalizationService.Instance.GetString("Voicemod_NoVoice", "Głos: Czysty / Domyślny");
            return $"{state.CurrentVoiceName} ({state.CurrentVoiceId})";
        }
    }

    public string VoicemodStatusMessageColor
    {
        get
        {
            if (VoicemodStatusMessage.Contains("Błąd", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("nie udało", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("Niepoprawne", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("Nie można", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }
            if (VoicemodStatusMessage.Contains("Pomyślnie", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("Połączono", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("sukces", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("zapisane", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("Wysłano", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("Wybrano", StringComparison.OrdinalIgnoreCase))
            {
                return "#10B981";
            }
            if (VoicemodStatusMessage.Contains("Łączenie", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("Connecting", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("Pobieranie", StringComparison.OrdinalIgnoreCase) ||
                VoicemodStatusMessage.Contains("Odświeżanie", StringComparison.OrdinalIgnoreCase))
            {
                return "#38BDF8";
            }
            return "#94A3B8";
        }
    }

    // --- HOME ASSISTANT SMART HOME ---
    private readonly IHomeAssistantService _homeAssistantService;

    [ObservableProperty]
    private string _homeAssistantServerUrl = "http://homeassistant.local:8123";

    [ObservableProperty]
    private string _homeAssistantAccessToken = "";

    [ObservableProperty]
    private bool _homeAssistantAutoConnect = true;

    [ObservableProperty]
    private string _homeAssistantStatusMessage = "";

    public static event Action? ScrollToHomeAssistantRequested;
    public static void RequestScrollToHomeAssistant() => ScrollToHomeAssistantRequested?.Invoke();

    public bool IsHomeAssistantConnected => _homeAssistantService.IsConnected;
    public int HomeAssistantEntityCount => _homeAssistantService.Entities.Count;

    public string HomeAssistantConnectionStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!_homeAssistantService.IsConnected)
                return loc.GetString("HA_Status_Disconnected", "ROZŁĄCZONO (Home Assistant offline)");
            return string.Format(loc.GetString("HA_Status_ConnectedWithCount", "POŁĄCZONO (Encje: {0})"), _homeAssistantService.Entities.Count);
        }
    }

    public string HomeAssistantConnectionStatusColor => _homeAssistantService.IsConnected ? "#10B981" : "#EF4444";

    public string HomeAssistantStatusMessageColor
    {
        get
        {
            if (HomeAssistantStatusMessage.Contains("Błąd", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("nie udało", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("Niepoprawne", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("Nie można", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return "#EF4444";
            }
            if (HomeAssistantStatusMessage.Contains("Pomyślnie", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("Połączono", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("sukces", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("zapisane", StringComparison.OrdinalIgnoreCase))
            {
                return "#10B981";
            }
            if (HomeAssistantStatusMessage.Contains("Łączenie", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("Connecting", StringComparison.OrdinalIgnoreCase) ||
                HomeAssistantStatusMessage.Contains("Pobieranie", StringComparison.OrdinalIgnoreCase))
            {
                return "#38BDF8";
            }
            return "#94A3B8";
        }
    }

    public SettingsPageViewModel(
        IMasterHubDeviceService deviceService,
        CanvasViewModel canvas,
        IDiscordService discordService,
        IObsService obsService,
        ITwitchService twitchService,
        IKickService kickService,
        IJetKvmService jetKvmService,
        ISpotifyService spotifyService,
        IVlcService vlcService,
        IVoicemodService voicemodService,
        IHomeAssistantService homeAssistantService,
        IProfileService profileService,
        ISystemControlService systemControl,
        IStartupService startupService)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _obsService = obsService ?? throw new ArgumentNullException(nameof(obsService));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _jetKvmService = jetKvmService ?? throw new ArgumentNullException(nameof(jetKvmService));
        _spotifyService = spotifyService ?? throw new ArgumentNullException(nameof(spotifyService));
        _vlcService = vlcService ?? throw new ArgumentNullException(nameof(vlcService));
        _voicemodService = voicemodService ?? throw new ArgumentNullException(nameof(voicemodService));
        _homeAssistantService = homeAssistantService ?? throw new ArgumentNullException(nameof(homeAssistantService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _systemControl = systemControl ?? throw new ArgumentNullException(nameof(systemControl));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));

        var appSettings = _profileService.GetAppSettings();
        _startWithWindows = _startupService.IsRunAtStartupEnabled();
        _startMinimizedToTray = appSettings.StartMinimizedToTray;

        string currentCode = LocalizationService.Instance.CurrentLanguageCode ?? appSettings.LanguageCode ?? "pl";
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == currentCode) ?? AvailableLanguages[0];

        var dc = _profileService.GetDiscordSettings();
        _discordClientId = dc.ClientId;
        _discordClientSecret = dc.ClientSecret;
        _discordAccessToken = dc.AccessToken;
        _discordMuteHotkey = dc.MuteHotkey;
        _discordDeafenHotkey = dc.DeafenHotkey;
        _discordUseRpc = dc.UseRpc;

        var obs = _profileService.GetObsSettings();
        _obsHost = obs.Host;
        _obsPort = obs.Port;
        _obsPassword = obs.Password;
        _obsAutoConnect = obs.AutoConnect;

        var twitch = _profileService.GetTwitchSettings();
        _twitchChannel = twitch.Channel;
        _twitchBotUsername = twitch.BotUsername;
        _twitchOAuthToken = twitch.OAuthToken;
        _twitchClientId = twitch.ClientId;
        _twitchAutoConnect = twitch.AutoConnect;

        var kick = _profileService.GetKickSettings();
        _kickChannel = kick.Channel;
        _kickBotUsername = kick.BotUsername;
        _kickAuthToken = kick.AuthToken;
        _kickAutoConnect = kick.AutoConnect;

        var jet = _profileService.GetJetKvmSettings();
        _jetKvmHost = jet.Host;
        _jetKvmPort = jet.Port;
        _jetKvmUseHttps = jet.UseHttps;
        _jetKvmAuthToken = jet.AuthToken;
        _jetKvmAutoConnect = jet.AutoConnect;

        var spotify = _profileService.GetSpotifySettings();
        _spotifyClientId = spotify.ClientId;
        _spotifyClientSecret = spotify.ClientSecret;
        _spotifyAutoConnect = spotify.AutoConnect;
        _spotifyFallbackToDesktopHotkeys = spotify.FallbackToDesktopHotkeys;

        var vlc = _profileService.GetVlcSettings();
        _vlcHost = vlc.Host;
        _vlcPort = vlc.Port;
        _vlcPassword = vlc.Password;
        _vlcAutoConnect = vlc.AutoConnect;
        _vlcFallbackToDesktopHotkeys = vlc.FallbackToDesktopHotkeys;

        var vm = _profileService.GetVoicemodSettings();
        _voicemodHost = vm.Host;
        _voicemodPort = vm.Port;
        _voicemodApiKey = vm.ApiKey;
        _voicemodAutoConnect = vm.AutoConnect;
        _voicemodFallbackToDesktopHotkeys = vm.FallbackToDesktopHotkeys;

        var ha = _profileService.GetHomeAssistantSettings();
        _homeAssistantServerUrl = ha.ServerUrl;
        _homeAssistantAccessToken = ha.AccessToken;
        _homeAssistantAutoConnect = ha.AutoConnect;

        _discordService.ConnectionStateChanged += OnDiscordStateChanged;
        _discordService.VoiceStateChanged += OnDiscordVoiceChanged;
        _obsService.ConnectionStateChanged += OnObsStateChanged;
        _twitchService.ConnectionStateChanged += OnTwitchStateChanged;
        _kickService.ConnectionStateChanged += OnKickStateChanged;
        _jetKvmService.ConnectionStateChanged += OnJetKvmStateChanged;
        _jetKvmService.PowerStateChanged += OnJetKvmPowerChanged;
        _spotifyService.ConnectionStateChanged += OnSpotifyConnectionChanged;
        _spotifyService.PlaybackStateChanged += OnSpotifyPlaybackChanged;
        _vlcService.ConnectionStateChanged += OnVlcConnectionChanged;
        _vlcService.PlaybackStateChanged += OnVlcPlaybackChanged;
        _voicemodService.ConnectionStateChanged += OnVoicemodConnectionChanged;
        _voicemodService.StateChanged += OnVoicemodStateChanged;
        _voicemodService.VoicesLoaded += OnVoicemodVoicesLoaded;
        _voicemodService.SoundsLoaded += OnVoicemodSoundsLoaded;
        _voicemodService.SoundPlayed += OnVoicemodSoundPlayed;
        _homeAssistantService.ConnectionStateChanged += OnHomeAssistantConnectionChanged;
        _homeAssistantService.EntitiesUpdated += OnHomeAssistantEntitiesUpdated;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnVoicemodConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(RefreshVoicemodProperties);
    }

    private void OnVoicemodStateChanged(VoicemodState state)
    {
        Dispatcher.UIThread.Post(RefreshVoicemodProperties);
    }

    private void OnVoicemodVoicesLoaded(IReadOnlyList<VoicemodVoiceItem> voices)
    {
        Dispatcher.UIThread.Post(RefreshVoicemodProperties);
    }

    private void OnVoicemodSoundsLoaded(IReadOnlyList<VoicemodSoundItem> sounds)
    {
        Dispatcher.UIThread.Post(RefreshVoicemodProperties);
    }

    private void OnVoicemodSoundPlayed(string soundName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            VoicemodStatusMessage = string.Format(LocalizationService.Instance.GetString("Voicemod_SoundPlayedSuccess", "Odtworzono efekt: {0}"), soundName);
        });
    }

    private void RefreshVoicemodProperties()
    {
        OnPropertyChanged(nameof(IsVoicemodConnected));
        OnPropertyChanged(nameof(VoicemodConnectionStatusText));
        OnPropertyChanged(nameof(VoicemodConnectionStatusColor));
        OnPropertyChanged(nameof(VoicemodVoiceChangerStatusText));
        OnPropertyChanged(nameof(VoicemodHearStatusText));
        OnPropertyChanged(nameof(VoicemodMicStatusText));
        OnPropertyChanged(nameof(VoicemodBgStatusText));
        OnPropertyChanged(nameof(VoicemodActiveVoiceText));
        OnPropertyChanged(nameof(VoicemodStatusMessageColor));
    }

    private void OnVlcConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(RefreshVlcProperties);
    }

    private void OnVlcPlaybackChanged(VlcPlaybackState state)
    {
        Dispatcher.UIThread.Post(RefreshVlcProperties);
    }

    private void RefreshVlcProperties()
    {
        OnPropertyChanged(nameof(IsVlcConnected));
        OnPropertyChanged(nameof(VlcConnectionStatusText));
        OnPropertyChanged(nameof(VlcConnectionStatusColor));
        OnPropertyChanged(nameof(VlcNowPlayingText));
        OnPropertyChanged(nameof(VlcPlaybackStatusText));
        OnPropertyChanged(nameof(VlcFullscreenStatusText));
        OnPropertyChanged(nameof(VlcStatusMessageColor));
    }

    private void OnSpotifyConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(RefreshSpotifyProperties);
    }

    private void OnSpotifyPlaybackChanged(SpotifyPlaybackState state)
    {
        Dispatcher.UIThread.Post(RefreshSpotifyProperties);
    }

    private void RefreshSpotifyProperties()
    {
        OnPropertyChanged(nameof(IsSpotifyConnected));
        OnPropertyChanged(nameof(SpotifyConnectionStatusText));
        OnPropertyChanged(nameof(SpotifyConnectionStatusColor));
        OnPropertyChanged(nameof(SpotifyNowPlayingText));
        OnPropertyChanged(nameof(SpotifyShuffleStatusText));
        OnPropertyChanged(nameof(SpotifyRepeatStatusText));
        OnPropertyChanged(nameof(SpotifyLikedStatusText));
        OnPropertyChanged(nameof(SpotifyStatusMessageColor));
    }

    private void OnJetKvmStateChanged(bool connected)
    {
        Dispatcher.UIThread.Post(RefreshJetKvmProperties);
    }

    private void OnJetKvmPowerChanged(JetKvmPowerState state)
    {
        Dispatcher.UIThread.Post(RefreshJetKvmProperties);
    }

    private void RefreshJetKvmProperties()
    {
        OnPropertyChanged(nameof(IsJetKvmConnected));
        OnPropertyChanged(nameof(IsJetKvmPowerOn));
        OnPropertyChanged(nameof(IsJetKvmHddActive));
        OnPropertyChanged(nameof(JetKvmConnectionStatusText));
        OnPropertyChanged(nameof(JetKvmConnectionStatusColor));
        OnPropertyChanged(nameof(JetKvmPowerStatusText));
        OnPropertyChanged(nameof(JetKvmPowerStatusColor));
        OnPropertyChanged(nameof(JetKvmStatusMessageColor));
    }

    private void OnDiscordStateChanged(bool connected)
    {
        Dispatcher.UIThread.Post(RefreshDiscordProperties);
    }

    private void OnDiscordVoiceChanged(bool muted, bool deafened)
    {
        Dispatcher.UIThread.Post(RefreshDiscordProperties);
    }

    private void RefreshDiscordProperties()
    {
        OnPropertyChanged(nameof(IsDiscordConnected));
        OnPropertyChanged(nameof(IsDiscordAuthenticated));
        OnPropertyChanged(nameof(DiscordUsername));
        OnPropertyChanged(nameof(DiscordLoggedInInfoDisplay));
        OnPropertyChanged(nameof(DiscordConnectionStatusText));
        OnPropertyChanged(nameof(DiscordConnectionStatusColor));
        OnPropertyChanged(nameof(ShowRpcAuthenticatedState));
        OnPropertyChanged(nameof(ShowRpcUnauthenticatedState));
    }

    private void OnTwitchStateChanged(bool connected)
    {
        Dispatcher.UIThread.Post(RefreshTwitchProperties);
    }

    private void RefreshTwitchProperties()
    {
        OnPropertyChanged(nameof(IsTwitchConnected));
        OnPropertyChanged(nameof(TwitchConnectionStatusText));
        OnPropertyChanged(nameof(TwitchConnectionStatusColor));
        OnPropertyChanged(nameof(TwitchStatusMessageColor));
    }

    private void OnKickStateChanged(bool connected)
    {
        Dispatcher.UIThread.Post(RefreshKickProperties);
    }

    private void RefreshKickProperties()
    {
        OnPropertyChanged(nameof(IsKickConnected));
        OnPropertyChanged(nameof(KickConnectionStatusText));
        OnPropertyChanged(nameof(KickConnectionStatusColor));
        OnPropertyChanged(nameof(KickStatusMessageColor));
    }

    partial void OnDiscordUseRpcChanged(bool value)
    {
        RefreshDiscordProperties();
        SaveDiscordSettings();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _startupService.SetRunAtStartup(value, StartMinimizedToTray);
        SaveAppSettings();
    }

    partial void OnStartMinimizedToTrayChanged(bool value)
    {
        if (StartWithWindows)
        {
            _startupService.SetRunAtStartup(true, value);
        }
        SaveAppSettings();
    }

    private void SaveAppSettings()
    {
        _profileService.SaveAppSettings(new AppSettingsModel
        {
            StartWithWindows = StartWithWindows,
            StartMinimizedToTray = StartMinimizedToTray,
            LanguageCode = SelectedLanguage?.Code ?? LocalizationService.Instance.CurrentLanguageCode ?? "pl"
        });
    }

    partial void OnSelectedLanguageChanged(MasterHub.Core.Localization.LanguageOption? value)
    {
        if (value != null)
        {
            LocalizationService.Instance.SetLanguage(value.Code);
            SaveAppSettings();
        }
    }

    [RelayCommand]
    public void ToggleStandby()
    {
        _deviceService.ToggleStandby();
    }

    [RelayCommand]
    public void SaveDiscordSettings()
    {
        _profileService.SaveDiscordSettings(new DiscordSettingsModel
        {
            ClientId = DiscordClientId,
            ClientSecret = DiscordClientSecret,
            AccessToken = DiscordAccessToken,
            MuteHotkey = DiscordMuteHotkey,
            DeafenHotkey = DiscordDeafenHotkey,
            UseRpc = DiscordUseRpc
        });
        DiscordStatusMessage = LocalizationService.Instance.GetString("Discord_SavedMsg", "Ustawienia Discorda zostały zapisane.");
    }

    [RelayCommand]
    public async Task AuthorizeDiscordAsync()
    {
        SaveDiscordSettings();
        if (string.IsNullOrWhiteSpace(DiscordClientId) || string.IsNullOrWhiteSpace(DiscordClientSecret))
        {
            DiscordStatusMessage = LocalizationService.Instance.GetString("Discord_ProvideCredentials", "Podaj Client ID oraz Client Secret z Discord Developer Portal.");
            return;
        }

        DiscordStatusMessage = LocalizationService.Instance.GetString("Discord_WaitingAuth", "Oczekiwanie na autoryzację w aplikacji Discord...");
        var (ok, token, error) = await _discordService.AuthorizeAndAuthenticateAsync(DiscordClientId, DiscordClientSecret);
        if (ok && !string.IsNullOrEmpty(token))
        {
            DiscordAccessToken = token;
            DiscordUseRpc = true;
            SaveDiscordSettings();
            RefreshDiscordProperties();
            DiscordStatusMessage = LocalizationService.Instance.GetString("Discord_AuthSuccess", "Pomyślnie zautoryzowano natywne Discord RPC! Działa bez skrótów.");
        }
        else
        {
            RefreshDiscordProperties();
            DiscordStatusMessage = string.Format(LocalizationService.Instance.GetString("Discord_AuthError", "Błąd autoryzacji: {0}"), error ?? LocalizationService.Instance.GetString("Discord_AuthDenied", "Odrzucono w aplikacji Discord"));
        }
    }

    [RelayCommand]
    public async Task ConnectDiscordAsync()
    {
        SaveDiscordSettings();
        DiscordStatusMessage = LocalizationService.Instance.GetString("Discord_Connecting", "Łączenie z Discordem...");
        bool ok = await _discordService.ConnectAsync(DiscordClientId, DiscordAccessToken);
        RefreshDiscordProperties();
        DiscordStatusMessage = ok
            ? LocalizationService.Instance.GetString("Discord_ConnectSuccess", "Pomyślnie nawiązano połączenie z Discordem!")
            : LocalizationService.Instance.GetString("Discord_ConnectFailed", "Nie udało się połączyć z potokiem Discord IPC.");
    }

    [RelayCommand]
    public async Task DisconnectDiscordAsync()
    {
        DiscordAccessToken = "";
        SaveDiscordSettings();
        await _discordService.DisconnectAsync();
        RefreshDiscordProperties();
        DiscordStatusMessage = LocalizationService.Instance.GetString("Discord_Disconnected", "Rozłączono sesję z Discordem.");
    }

    [RelayCommand]
    public void TestMute()
    {
        SaveDiscordSettings();
        if (DiscordUseRpc && _discordService.IsConnected && _discordService.IsAuthenticated)
        {
            _ = _discordService.ToggleMuteAsync();
            DiscordStatusMessage = LocalizationService.Instance.GetString("Discord_MuteToggledRpc", "Przełączono wyciszenie mikrofonu przez Discord RPC!");
        }
        else
        {
            _systemControl.SendHotkey(DiscordMuteHotkey);
            DiscordStatusMessage = string.Format(LocalizationService.Instance.GetString("Discord_MuteHotkeySent", "Wysłano skrót klawiszowy wyciszenia: {0}"), DiscordMuteHotkey);
        }
    }

    [RelayCommand]
    public void TestDeafen()
    {
        SaveDiscordSettings();
        if (DiscordUseRpc && _discordService.IsConnected && _discordService.IsAuthenticated)
        {
            _ = _discordService.ToggleDeafenAsync();
            DiscordStatusMessage = LocalizationService.Instance.GetString("Discord_DeafenToggledRpc", "Przełączono wyciszenie odsłuchu przez Discord RPC!");
        }
        else
        {
            _systemControl.SendHotkey(DiscordDeafenHotkey);
            DiscordStatusMessage = string.Format(LocalizationService.Instance.GetString("Discord_DeafenHotkeySent", "Wysłano skrót klawiszowy odsłuchu: {0}"), DiscordDeafenHotkey);
        }
    }

    private void OnObsStateChanged(bool connected)
    {
        Dispatcher.UIThread.Post(RefreshObsProperties);
    }

    private void RefreshObsProperties()
    {
        OnPropertyChanged(nameof(IsObsConnected));
        OnPropertyChanged(nameof(ObsVersion));
        OnPropertyChanged(nameof(ObsConnectionStatusText));
        OnPropertyChanged(nameof(ObsConnectionStatusColor));
    }

    [RelayCommand]
    public void SaveObsSettings()
    {
        _profileService.SaveObsSettings(new ObsSettingsModel
        {
            Host = ObsHost,
            Port = ObsPort,
            Password = ObsPassword,
            AutoConnect = ObsAutoConnect
        });
        ObsStatusMessage = LocalizationService.Instance.GetString("Obs_SettingsSaved", "Ustawienia OBS Studio zostały zapisane.");
    }

    [RelayCommand]
    public async Task ConnectObsAsync()
    {
        SaveObsSettings();
        ObsStatusMessage = LocalizationService.Instance.GetString("Obs_Connecting", "Łączenie z serwerem OBS WebSocket...");
        OnPropertyChanged(nameof(ObsStatusMessageColor));

        int port = ObsPort > 0 ? ObsPort : 4455;
        bool ok = await _obsService.ConnectAsync(ObsHost, port, ObsPassword);
        RefreshObsProperties();
        if (ok)
        {
            ObsStatusMessage = LocalizationService.Instance.GetString("Obs_ConnectSuccess", "Pomyślnie połączono z OBS Studio!");
        }
        else
        {
            string err = _obsService.LastError ?? "";
            if (!string.IsNullOrWhiteSpace(err))
            {
                ObsStatusMessage = err;
            }
            else
            {
                ObsStatusMessage = LocalizationService.Instance.GetString("Obs_ConnectFailed", "Nie udało się połączyć z OBS Studio. Upewnij się, że OBS jest włączony, a serwer WebSocket jest aktywny.");
            }
        }
        OnPropertyChanged(nameof(ObsStatusMessageColor));
    }

    [RelayCommand]
    public void AutoDetectObsSettings()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string obsCfgPath = System.IO.Path.Combine(appData, "obs-studio", "plugin_config", "obs-websocket", "config.json");
            if (System.IO.File.Exists(obsCfgPath))
            {
                string json = System.IO.File.ReadAllText(obsCfgPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("server_port", out var pProp) && pProp.TryGetInt32(out int port))
                {
                    ObsPort = port;
                }
                if (root.TryGetProperty("server_password", out var pwProp))
                {
                    ObsPassword = pwProp.GetString() ?? "";
                }
                ObsHost = "localhost";
                SaveObsSettings();
                ObsStatusMessage = LocalizationService.Instance.GetString("Obs_AutoDetected", "Automatycznie wczytano konfigurację z programu OBS Studio (localhost, port i hasło).");
                OnPropertyChanged(nameof(ObsStatusMessageColor));
                return;
            }
        }
        catch { }

        ObsStatusMessage = LocalizationService.Instance.GetString("Obs_AutoDetectFailed", "Nie znaleziono pliku konfiguracyjnego OBS w %APPDATA%\\obs-studio.");
        OnPropertyChanged(nameof(ObsStatusMessageColor));
    }

    [RelayCommand]
    public async Task DisconnectObsAsync()
    {
        await _obsService.DisconnectAsync();
        RefreshObsProperties();
        ObsStatusMessage = LocalizationService.Instance.GetString("Obs_DisconnectedMsg", "Rozłączono z OBS Studio.");
    }

    [RelayCommand]
    public async Task TestObsStreamAsync()
    {
        if (!_obsService.IsConnected)
        {
            await ConnectObsAsync();
        }
        if (_obsService.IsConnected)
        {
            await _obsService.ToggleStreamAsync();
            ObsStatusMessage = LocalizationService.Instance.GetString("Obs_StreamToggled", "Wysłano polecenie przełączenia streamu.");
        }
    }

    [RelayCommand]
    public async Task TestObsRecordAsync()
    {
        if (!_obsService.IsConnected)
        {
            await ConnectObsAsync();
        }
        if (_obsService.IsConnected)
        {
            await _obsService.ToggleRecordAsync();
            ObsStatusMessage = LocalizationService.Instance.GetString("Obs_RecordToggled", "Wysłano polecenie przełączenia nagrywania.");
        }
    }

    // --- TWITCH COMMANDS ---
    [RelayCommand]
    public void SaveTwitchSettings()
    {
        _profileService.SaveTwitchSettings(new TwitchSettingsModel
        {
            Channel = TwitchChannel?.Trim() ?? "",
            BotUsername = TwitchBotUsername?.Trim() ?? "",
            OAuthToken = TwitchOAuthToken?.Trim() ?? "",
            ClientId = TwitchClientId?.Trim() ?? "",
            AutoConnect = TwitchAutoConnect
        });
        TwitchStatusMessage = LocalizationService.Instance.GetString("Twitch_SavedMsg", "Ustawienia Twitcha zostały zapisane.");
    }

    [RelayCommand]
    public async Task ConnectTwitchAsync()
    {
        SaveTwitchSettings();
        if (string.IsNullOrWhiteSpace(TwitchChannel))
        {
            TwitchStatusMessage = LocalizationService.Instance.GetString("Twitch_ChannelRequired", "Podaj nazwę kanału Twitch.");
            return;
        }

        TwitchStatusMessage = LocalizationService.Instance.GetString("Twitch_ConnectingMsg", "Łączenie z czatem Twitch...");
        bool ok = await _twitchService.ConnectAsync(TwitchChannel, TwitchOAuthToken, TwitchBotUsername, TwitchClientId);
        RefreshTwitchProperties();
        if (ok)
        {
            TwitchStatusMessage = string.Format(LocalizationService.Instance.GetString("Twitch_ConnectedMsg", "Połączono pomyślnie z kanałem #{0}."), TwitchChannel);
        }
        else
        {
            TwitchStatusMessage = LocalizationService.Instance.GetString("Twitch_ConnectError", "Nie udało się połączyć. Sprawdź nazwę kanału i poprawność tokenu OAuth.");
        }
    }

    [RelayCommand]
    public async Task DisconnectTwitchAsync()
    {
        await _twitchService.DisconnectAsync();
        RefreshTwitchProperties();
        TwitchStatusMessage = LocalizationService.Instance.GetString("Twitch_DisconnectedMsg", "Rozłączono z Twitch.");
    }

    [RelayCommand]
    public async Task TestTwitchMessageAsync()
    {
        if (!_twitchService.IsConnected)
        {
            await ConnectTwitchAsync();
        }
        if (_twitchService.IsConnected)
        {
            string msg = LocalizationService.Instance.GetString("Twitch_TestChatMessage", "MasterHub: Test połączenia czatu udany! 🚀");
            bool sent = await _twitchService.SendChatMessageAsync(msg);
            TwitchStatusMessage = sent
                ? LocalizationService.Instance.GetString("Twitch_TestSentOk", "Wiadomość testowa została wysłana na czat!")
                : LocalizationService.Instance.GetString("Twitch_TestSendFailed", "Nie udało się wysłać wiadomości. Upewnij się, że token OAuth ma uprawnienie chat:write.");
        }
    }

    [RelayCommand]
    public async Task TestTwitchClipAsync()
    {
        if (!_twitchService.IsConnected)
        {
            await ConnectTwitchAsync();
        }
        TwitchStatusMessage = LocalizationService.Instance.GetString("Twitch_CreatingClip", "Tworzenie klipu przez Twitch Helix API...");
        var res = await _twitchService.CreateClipAsync();
        if (res != null && res.Success)
        {
            TwitchStatusMessage = string.Format(LocalizationService.Instance.GetString("Twitch_ClipCreatedOk", "Klip utworzony! URL: {0}"), res.EditUrl ?? res.ClipId);
        }
        else
        {
            TwitchStatusMessage = string.Format(LocalizationService.Instance.GetString("Twitch_ClipFailed", "Błąd klipu: {0}"), res?.ErrorMessage ?? "Transmisja musi być na żywo.");
        }
    }

    // --- KICK COMMANDS ---
    [RelayCommand]
    public void SaveKickSettings()
    {
        _profileService.SaveKickSettings(new KickSettingsModel
        {
            Channel = KickChannel?.Trim() ?? "",
            BotUsername = KickBotUsername?.Trim() ?? "",
            AuthToken = KickAuthToken?.Trim() ?? "",
            AutoConnect = KickAutoConnect
        });
        KickStatusMessage = LocalizationService.Instance.GetString("Kick_SavedMsg", "Ustawienia Kick zostały zapisane.");
    }

    [RelayCommand]
    public async Task ConnectKickAsync()
    {
        SaveKickSettings();
        if (string.IsNullOrWhiteSpace(KickChannel))
        {
            KickStatusMessage = LocalizationService.Instance.GetString("Kick_ChannelRequired", "Podaj nazwę kanału Kick.");
            return;
        }

        KickStatusMessage = LocalizationService.Instance.GetString("Kick_ConnectingMsg", "Łączenie z Kick (Pusher WebSocket)...");
        bool ok = await _kickService.ConnectAsync(KickChannel, KickAuthToken, KickBotUsername);
        RefreshKickProperties();
        if (ok)
        {
            KickStatusMessage = string.Format(LocalizationService.Instance.GetString("Kick_ConnectedMsg", "Połączono pomyślnie z kanałem kick.com/{0}."), KickChannel);
        }
        else
        {
            KickStatusMessage = LocalizationService.Instance.GetString("Kick_ConnectError", "Nie udało się połączyć z kanałem Kick.");
        }
    }

    [RelayCommand]
    public async Task DisconnectKickAsync()
    {
        await _kickService.DisconnectAsync();
        RefreshKickProperties();
        KickStatusMessage = LocalizationService.Instance.GetString("Kick_DisconnectedMsg", "Rozłączono z Kick.");
    }

    [RelayCommand]
    public async Task TestKickMessageAsync()
    {
        if (!_kickService.IsConnected)
        {
            await ConnectKickAsync();
        }
        if (_kickService.IsConnected)
        {
            string msg = LocalizationService.Instance.GetString("Kick_TestChatMessage", "MasterHub: Test połączenia z Kick udany! 🚀");
            bool sent = await _kickService.SendChatMessageAsync(msg);
            KickStatusMessage = sent
                ? LocalizationService.Instance.GetString("Kick_TestSentOk", "Wiadomość testowa wysłana na czat Kick!")
                : LocalizationService.Instance.GetString("Kick_TestSendFailed", "Wymagany token autoryzacji Kick API / bota, aby pisać na czacie.");
        }
    }

    [RelayCommand]
    public async Task TestKickClipAsync()
    {
        if (!_kickService.IsConnected)
        {
            await ConnectKickAsync();
        }
        var res = await _kickService.CreateClipAsync();
        if (res != null && res.Success)
        {
            KickStatusMessage = string.Format(LocalizationService.Instance.GetString("Kick_ClipCreatedOk", "Klip utworzony: {0}"), res.ClipUrl);
        }
        else
        {
            KickStatusMessage = string.Format(LocalizationService.Instance.GetString("Kick_ClipFailed", "Błąd klipu: {0}"), res?.ErrorMessage ?? "Nie udało się utworzyć klipu.");
        }
    }

    // --- JETKVM COMMANDS ---
    [RelayCommand]
    public void SaveJetKvmSettings()
    {
        var settings = new JetKvmSettingsModel
        {
            Host = JetKvmHost?.Trim() ?? "192.168.1.100",
            Port = JetKvmPort > 0 ? JetKvmPort : 80,
            UseHttps = JetKvmUseHttps,
            AuthToken = JetKvmAuthToken?.Trim() ?? "",
            AutoConnect = JetKvmAutoConnect
        };
        _profileService.SaveJetKvmSettings(settings);
        _jetKvmService.Configure(settings);
        JetKvmStatusMessage = LocalizationService.Instance.GetString("JetKvm_SavedMsg", "Ustawienia JetKVM zostały zapisane.");
    }

    [RelayCommand]
    public async Task TestJetKvmConnectionAsync()
    {
        SaveJetKvmSettings();
        JetKvmStatusMessage = LocalizationService.Instance.GetString("JetKvm_ConnectingMsg", "Sprawdzanie połączenia z JetKVM...");
        var (success, message) = await _jetKvmService.TestConnectionDetailedAsync();
        RefreshJetKvmProperties();
        JetKvmStatusMessage = message;
    }

    [RelayCommand]
    public async Task DisconnectJetKvmAsync()
    {
        await _jetKvmService.DisconnectAsync();
        RefreshJetKvmProperties();
        JetKvmStatusMessage = LocalizationService.Instance.GetString("JetKvm_DisconnectedMsg", "Rozłączono z JetKVM.");
    }

    [RelayCommand]
    public async Task TestJetKvmPowerShortAsync()
    {
        SaveJetKvmSettings();
        bool ok = await _jetKvmService.PowerShortPressAsync();
        RefreshJetKvmProperties();
        JetKvmStatusMessage = ok
            ? LocalizationService.Instance.GetString("JetKvm_ShortSentOk", "Wysłano sygnał włączenia / ACPI (krótki impuls).")
            : LocalizationService.Instance.GetString("JetKvm_ActionFailed", "Nie udało się wysłać polecenia do JetKVM.");
    }

    [RelayCommand]
    public async Task TestJetKvmPowerLongAsync()
    {
        SaveJetKvmSettings();
        bool ok = await _jetKvmService.PowerLongPressAsync();
        RefreshJetKvmProperties();
        JetKvmStatusMessage = ok
            ? LocalizationService.Instance.GetString("JetKvm_LongSentOk", "Wysłano sygnał wymuszonego wyłączenia (5s przytrzymania).")
            : LocalizationService.Instance.GetString("JetKvm_ActionFailed", "Nie udało się wysłać polecenia do JetKVM.");
    }

    [RelayCommand]
    public async Task TestJetKvmResetAsync()
    {
        SaveJetKvmSettings();
        bool ok = await _jetKvmService.ResetAsync();
        RefreshJetKvmProperties();
        JetKvmStatusMessage = ok
            ? LocalizationService.Instance.GetString("JetKvm_ResetSentOk", "Wysłano sygnał resetu sprzętowego.")
            : LocalizationService.Instance.GetString("JetKvm_ActionFailed", "Nie udało się wysłać polecenia do JetKVM.");
    }

    [RelayCommand]
    public async Task RefreshJetKvmStateAsync()
    {
        await _jetKvmService.RefreshStateAsync();
        RefreshJetKvmProperties();
    }

    // --- SPOTIFY COMMANDS ---
    [RelayCommand]
    public void SaveSpotifySettings()
    {
        var settings = new SpotifySettingsModel
        {
            ClientId = SpotifyClientId?.Trim() ?? "",
            ClientSecret = SpotifyClientSecret?.Trim() ?? "",
            AutoConnect = SpotifyAutoConnect,
            FallbackToDesktopHotkeys = SpotifyFallbackToDesktopHotkeys
        };
        _profileService.SaveSpotifySettings(settings);
        _spotifyService.Configure(settings);
        SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_SavedMsg", "Ustawienia Spotify zostały zapisane.");
    }

    [RelayCommand]
    public async Task LoginSpotifyAsync()
    {
        SaveSpotifySettings();
        SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_LoginStarting", "Oczekiwanie na autoryzację w przeglądarce...");
        var (success, message) = await _spotifyService.StartPkceLoginAsync(url => _systemControl.OpenWebsite(url));
        RefreshSpotifyProperties();
        if (success)
        {
            SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_LoginSuccess", "Pomyślnie połączono z kontem Spotify!");
            await _spotifyService.RefreshPlaybackStateAsync();
            RefreshSpotifyProperties();
        }
        else
        {
            SpotifyStatusMessage = string.Format(LocalizationService.Instance.GetString("Spotify_LoginFailed", "Błąd logowania Spotify: {0}"), message ?? "Nieznany błąd");
        }
    }

    [RelayCommand]
    public async Task DisconnectSpotifyAsync()
    {
        await _spotifyService.DisconnectAsync();
        RefreshSpotifyProperties();
        SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_DisconnectedMsg", "Rozłączono ze Spotify Web API.");
    }

    [RelayCommand]
    public async Task RefreshSpotifyStateAsync()
    {
        await _spotifyService.RefreshPlaybackStateAsync();
        RefreshSpotifyProperties();
    }

    [RelayCommand]
    public async Task TestSpotifyShuffleAsync()
    {
        bool ok = await _spotifyService.ToggleShuffleAsync();
        RefreshSpotifyProperties();
        SpotifyStatusMessage = ok
            ? LocalizationService.Instance.GetString("Spotify_ShuffleToggled", "Przełączono tryb odtwarzania losowego (Shuffle).")
            : LocalizationService.Instance.GetString("Spotify_ActionFailed", "Nie udało się wykonać akcji Spotify.");
    }

    [RelayCommand]
    public async Task TestSpotifyRepeatAsync()
    {
        var mode = await _spotifyService.ToggleRepeatAsync();
        RefreshSpotifyProperties();
        SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_RepeatToggled", "Przełączono tryb powtarzania (Repeat).");
    }

    [RelayCommand]
    public async Task TestSpotifyLikeAsync()
    {
        bool ok = await _spotifyService.ToggleLikeTrackAsync();
        RefreshSpotifyProperties();
        SpotifyStatusMessage = ok
            ? LocalizationService.Instance.GetString("Spotify_LikeToggled", "Zaktualizowano stan polubienia utworu (Polubione utwory).")
            : LocalizationService.Instance.GetString("Spotify_ActionFailed", "Nie udało się wykonać akcji Spotify.");
    }

    [RelayCommand]
    public async Task TestSpotifyPlayUriAsync()
    {
        string uri = SpotifyTestUri?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(uri))
        {
            SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_UriEmpty", "Wprowadź poprawny URI lub link Spotify.");
            return;
        }

        bool ok = await _spotifyService.PlayUriAsync(uri);
        RefreshSpotifyProperties();
        SpotifyStatusMessage = ok
            ? LocalizationService.Instance.GetString("Spotify_PlayUriSent", "Wysłano polecenie odtworzenia URI do Spotify!")
            : LocalizationService.Instance.GetString("Spotify_ActionFailed", "Nie udało się wykonać akcji Spotify.");
    }

    [RelayCommand]
    public async Task TestSpotifyAddToQueueAsync()
    {
        string uri = SpotifyTestUri?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(uri))
        {
            SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_UriEmpty", "Wprowadź poprawny URI lub link Spotify.");
            return;
        }

        bool ok = await _spotifyService.AddToQueueAsync(uri);
        RefreshSpotifyProperties();
        SpotifyStatusMessage = ok
            ? LocalizationService.Instance.GetString("Spotify_QueueSent", "Dodano wskazany utwór do kolejki odtwarzania Spotify!")
            : LocalizationService.Instance.GetString("Spotify_ActionFailed", "Nie udało się wykonać akcji Spotify.");
    }

    [RelayCommand]
    public async Task LoadSpotifyPlaylistsAsync()
    {
        if (!_spotifyService.IsConnected)
        {
            SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_Status_Disconnected", "Brak autoryzacji Spotify Web API.");
            return;
        }

        SpotifyStatusMessage = LocalizationService.Instance.GetString("Spotify_LoadingPlaylists", "Pobieranie playlist konta Spotify...");
        var playlists = await _spotifyService.GetUserPlaylistsAsync();
        SpotifyPlaylists.Clear();
        foreach (var p in playlists)
        {
            SpotifyPlaylists.Add(p);
        }

        SpotifyStatusMessage = string.Format(LocalizationService.Instance.GetString("Spotify_PlaylistsLoaded", "Pobrano {0} playlist. Skopiuj URI do konfiguracji klawiszy."), SpotifyPlaylists.Count);
    }

    // --- VLC COMMANDS ---
    [RelayCommand]
    public void SaveVlcSettings()
    {
        var settings = new VlcSettingsModel
        {
            Host = string.IsNullOrWhiteSpace(VlcHost) ? "127.0.0.1" : VlcHost.Trim(),
            Port = VlcPort <= 0 ? 8080 : VlcPort,
            Password = VlcPassword ?? "",
            AutoConnect = VlcAutoConnect,
            FallbackToDesktopHotkeys = VlcFallbackToDesktopHotkeys
        };
        _profileService.SaveVlcSettings(settings);
        _vlcService.Configure(settings);
        VlcStatusMessage = LocalizationService.Instance.GetString("Vlc_SavedMsg", "Ustawienia VLC zostały zapisane.");
    }

    [RelayCommand]
    public async Task TestVlcConnectionAsync()
    {
        SaveVlcSettings();
        VlcStatusMessage = LocalizationService.Instance.GetString("Vlc_ConnectingMsg", "Łączenie z VLC HTTP...");
        var (success, message) = await _vlcService.TestConnectionDetailedAsync();
        RefreshVlcProperties();
        VlcStatusMessage = message;
    }

    [RelayCommand]
    public async Task DisconnectVlcAsync()
    {
        await _vlcService.DisconnectAsync();
        RefreshVlcProperties();
        VlcStatusMessage = LocalizationService.Instance.GetString("Vlc_DisconnectedMsg", "Rozłączono z VLC.");
    }

    [RelayCommand]
    public async Task RefreshVlcStateAsync()
    {
        VlcStatusMessage = LocalizationService.Instance.GetString("Vlc_RefreshingMsg", "Pobieranie stanu odtwarzacza VLC...");
        await _vlcService.RefreshPlaybackStateAsync();
        RefreshVlcProperties();
        VlcStatusMessage = LocalizationService.Instance.GetString("Vlc_RefreshedMsg", "Zaktualizowano stan VLC.");
    }

    [RelayCommand]
    public async Task TestVlcPlayPauseAsync()
    {
        bool ok = await _vlcService.PlayPauseAsync();
        RefreshVlcProperties();
        VlcStatusMessage = ok
            ? LocalizationService.Instance.GetString("Vlc_CmdPlayPauseSuccess", "Wysłano: Odtwarzaj / Wstrzymaj (VLC).")
            : LocalizationService.Instance.GetString("Vlc_CmdFailed", "Nie udało się wykonać akcji w VLC.");
    }

    [RelayCommand]
    public async Task TestVlcStopAsync()
    {
        bool ok = await _vlcService.StopAsync();
        RefreshVlcProperties();
        VlcStatusMessage = ok
            ? LocalizationService.Instance.GetString("Vlc_CmdStopSuccess", "Wysłano: Zatrzymaj odtwarzanie (VLC).")
            : LocalizationService.Instance.GetString("Vlc_CmdFailed", "Nie udało się wykonać akcji w VLC.");
    }

    [RelayCommand]
    public async Task TestVlcNextAsync()
    {
        bool ok = await _vlcService.NextTrackAsync();
        RefreshVlcProperties();
        VlcStatusMessage = ok
            ? LocalizationService.Instance.GetString("Vlc_CmdNextSuccess", "Wysłano: Następny utwór (VLC).")
            : LocalizationService.Instance.GetString("Vlc_CmdFailed", "Nie udało się wykonać akcji w VLC.");
    }

    [RelayCommand]
    public async Task TestVlcPreviousAsync()
    {
        bool ok = await _vlcService.PreviousTrackAsync();
        RefreshVlcProperties();
        VlcStatusMessage = ok
            ? LocalizationService.Instance.GetString("Vlc_CmdPrevSuccess", "Wysłano: Poprzedni utwór (VLC).")
            : LocalizationService.Instance.GetString("Vlc_CmdFailed", "Nie udało się wykonać akcji w VLC.");
    }

    [RelayCommand]
    public async Task TestVlcFullscreenAsync()
    {
        bool ok = await _vlcService.ToggleFullscreenAsync();
        RefreshVlcProperties();
        VlcStatusMessage = ok
            ? LocalizationService.Instance.GetString("Vlc_CmdFullscreenSuccess", "Wysłano: Przełącz pełny ekran (VLC).")
            : LocalizationService.Instance.GetString("Vlc_CmdFailed", "Nie udało się wykonać akcji w VLC.");
    }

    // --- VOICEMOD COMMANDS ---
    [RelayCommand]
    public void SaveVoicemodSettings()
    {
        var settings = new VoicemodSettingsModel
        {
            Host = string.IsNullOrWhiteSpace(VoicemodHost) ? "127.0.0.1" : VoicemodHost.Trim(),
            Port = VoicemodPort <= 0 ? 59129 : VoicemodPort,
            ApiKey = VoicemodApiKey ?? "",
            AutoConnect = VoicemodAutoConnect,
            FallbackToDesktopHotkeys = VoicemodFallbackToDesktopHotkeys
        };
        _profileService.SaveVoicemodSettings(settings);
        _voicemodService.Configure(settings);
        VoicemodStatusMessage = LocalizationService.Instance.GetString("Voicemod_SavedMsg", "Ustawienia Voicemod zostały zapisane.");
    }

    [RelayCommand]
    public async Task ConnectVoicemodAsync()
    {
        SaveVoicemodSettings();
        VoicemodStatusMessage = LocalizationService.Instance.GetString("Voicemod_ConnectingMsg", "Łączenie z Voicemod API...");
        var (success, message) = await _voicemodService.ConnectAsync();
        RefreshVoicemodProperties();
        VoicemodStatusMessage = message;
    }

    [RelayCommand]
    public async Task DisconnectVoicemodAsync()
    {
        await _voicemodService.DisconnectAsync();
        RefreshVoicemodProperties();
        VoicemodStatusMessage = LocalizationService.Instance.GetString("Voicemod_DisconnectedMsg", "Rozłączono z Voicemod.");
    }

    [RelayCommand]
    public async Task RefreshVoicemodStateAsync()
    {
        VoicemodStatusMessage = LocalizationService.Instance.GetString("Voicemod_RefreshingMsg", "Odświeżanie stanu Voicemod...");
        await _voicemodService.RefreshStateAsync();
        RefreshVoicemodProperties();
        VoicemodStatusMessage = LocalizationService.Instance.GetString("Voicemod_RefreshedMsg", "Zaktualizowano stan Voicemod.");
    }

    [RelayCommand]
    public async Task TestVoicemodToggleVoiceChangerAsync()
    {
        bool ok = await _voicemodService.ToggleVoiceChangerAsync();
        RefreshVoicemodProperties();
        VoicemodStatusMessage = ok
            ? LocalizationService.Instance.GetString("Voicemod_CmdVoiceToggleSuccess", "Wysłano: Przełącz modulację głosu.")
            : LocalizationService.Instance.GetString("Voicemod_CmdFailed", "Nie udało się wykonać akcji w Voicemod.");
    }

    [RelayCommand]
    public async Task TestVoicemodToggleHearAsync()
    {
        bool ok = await _voicemodService.ToggleHearMyVoiceAsync();
        RefreshVoicemodProperties();
        VoicemodStatusMessage = ok
            ? LocalizationService.Instance.GetString("Voicemod_CmdHearToggleSuccess", "Wysłano: Przełącz odsłuch głosu.")
            : LocalizationService.Instance.GetString("Voicemod_CmdFailed", "Nie udało się wykonać akcji w Voicemod.");
    }

    [RelayCommand]
    public async Task TestVoicemodToggleMuteAsync()
    {
        bool ok = await _voicemodService.ToggleMuteMicAsync();
        RefreshVoicemodProperties();
        VoicemodStatusMessage = ok
            ? LocalizationService.Instance.GetString("Voicemod_CmdMuteToggleSuccess", "Wysłano: Przełącz wyciszenie mikrofonu.")
            : LocalizationService.Instance.GetString("Voicemod_CmdFailed", "Nie udało się wykonać akcji w Voicemod.");
    }

    [RelayCommand]
    public async Task TestVoicemodToggleBgAsync()
    {
        bool ok = await _voicemodService.ToggleBackgroundEffectsAsync();
        RefreshVoicemodProperties();
        VoicemodStatusMessage = ok
            ? LocalizationService.Instance.GetString("Voicemod_CmdBgToggleSuccess", "Wysłano: Przełącz dźwięk tła głosu.")
            : LocalizationService.Instance.GetString("Voicemod_CmdFailed", "Nie udało się wykonać akcji w Voicemod.");
    }

    private void OnHomeAssistantConnectionChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(RefreshHomeAssistantProperties);
    }

    private void OnHomeAssistantEntitiesUpdated(object? sender, IReadOnlyList<HomeAssistantEntityModel> entities)
    {
        Dispatcher.UIThread.Post(RefreshHomeAssistantProperties);
    }

    [RelayCommand]
    public async Task ConnectHomeAssistantAsync()
    {
        SaveHomeAssistantSettings();
        HomeAssistantStatusMessage = LocalizationService.Instance.GetString("HA_Connecting", "Łączenie z Home Assistant...");
        OnPropertyChanged(nameof(HomeAssistantStatusMessageColor));
        bool ok = await _homeAssistantService.ConnectAsync();
        HomeAssistantStatusMessage = ok
            ? LocalizationService.Instance.GetString("HA_ConnectSuccess", "Połączono pomyślnie z Home Assistant!")
            : LocalizationService.Instance.GetString("HA_ConnectFailed", "Nie udało się połączyć z Home Assistant. Sprawdź URL i token.");
        OnPropertyChanged(nameof(HomeAssistantStatusMessageColor));
        RefreshHomeAssistantProperties();
    }

    [RelayCommand]
    public async Task DisconnectHomeAssistantAsync()
    {
        await _homeAssistantService.DisconnectAsync();
        HomeAssistantStatusMessage = LocalizationService.Instance.GetString("HA_DisconnectedMsg", "Rozłączono z Home Assistant.");
        OnPropertyChanged(nameof(HomeAssistantStatusMessageColor));
        RefreshHomeAssistantProperties();
    }

    [RelayCommand]
    public async Task TestHomeAssistantConnectionAsync()
    {
        HomeAssistantStatusMessage = LocalizationService.Instance.GetString("HA_Testing", "Testowanie połączenia...");
        OnPropertyChanged(nameof(HomeAssistantStatusMessageColor));
        bool ok = await _homeAssistantService.ConnectAsync();
        HomeAssistantStatusMessage = ok
            ? string.Format(LocalizationService.Instance.GetString("HA_TestOk", "Połączenie OK! Załadowano {0} encji."), _homeAssistantService.Entities.Count)
            : LocalizationService.Instance.GetString("HA_TestFail", "Błąd połączenia. Upewnij się, że Home Assistant działa.");
        OnPropertyChanged(nameof(HomeAssistantStatusMessageColor));
        RefreshHomeAssistantProperties();
    }

    [RelayCommand]
    public void SaveHomeAssistantSettings()
    {
        var model = new HomeAssistantSettingsModel
        {
            ServerUrl = HomeAssistantServerUrl,
            AccessToken = HomeAssistantAccessToken,
            AutoConnect = HomeAssistantAutoConnect
        };
        _profileService.SaveHomeAssistantSettings(model);
        HomeAssistantStatusMessage = LocalizationService.Instance.GetString("HA_SettingsSaved", "Ustawienia Home Assistant zostały zapisane.");
        OnPropertyChanged(nameof(HomeAssistantStatusMessageColor));
    }

    public void RefreshHomeAssistantProperties()
    {
        OnPropertyChanged(nameof(IsHomeAssistantConnected));
        OnPropertyChanged(nameof(HomeAssistantConnectionStatusText));
        OnPropertyChanged(nameof(HomeAssistantConnectionStatusColor));
        OnPropertyChanged(nameof(HomeAssistantEntityCount));
    }

    private void OnLanguageChanged(string _)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshDiscordProperties();
            RefreshObsProperties();
            RefreshTwitchProperties();
            RefreshKickProperties();
            RefreshJetKvmProperties();
            RefreshSpotifyProperties();
            RefreshVlcProperties();
            RefreshVoicemodProperties();
            RefreshHomeAssistantProperties();
        });
    }

    public void Dispose()
    {
        _discordService.ConnectionStateChanged -= OnDiscordStateChanged;
        _discordService.VoiceStateChanged -= OnDiscordVoiceChanged;
        _obsService.ConnectionStateChanged -= OnObsStateChanged;
        _twitchService.ConnectionStateChanged -= OnTwitchStateChanged;
        _kickService.ConnectionStateChanged -= OnKickStateChanged;
        _jetKvmService.ConnectionStateChanged -= OnJetKvmStateChanged;
        _jetKvmService.PowerStateChanged -= OnJetKvmPowerChanged;
        _spotifyService.ConnectionStateChanged -= OnSpotifyConnectionChanged;
        _spotifyService.PlaybackStateChanged -= OnSpotifyPlaybackChanged;
        _vlcService.ConnectionStateChanged -= OnVlcConnectionChanged;
        _vlcService.PlaybackStateChanged -= OnVlcPlaybackChanged;
        _voicemodService.ConnectionStateChanged -= OnVoicemodConnectionChanged;
        _voicemodService.StateChanged -= OnVoicemodStateChanged;
        _voicemodService.VoicesLoaded -= OnVoicemodVoicesLoaded;
        _voicemodService.SoundsLoaded -= OnVoicemodSoundsLoaded;
        _voicemodService.SoundPlayed -= OnVoicemodSoundPlayed;
        _homeAssistantService.ConnectionStateChanged -= OnHomeAssistantConnectionChanged;
        _homeAssistantService.EntitiesUpdated -= OnHomeAssistantEntitiesUpdated;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
}

using System;
using System.Threading;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.Obs;

namespace MasterHub.Core.Devices;

/// <summary>Which situations the base's edge light may react to. Each reaction can be switched off on its own; the whole thing is off by default.</summary>
public class StatusLightSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>Red while the default microphone is muted.</summary>
    public bool MicMuted { get; set; } = true;

    /// <summary>Red while OBS is streaming or recording.</summary>
    public bool ObsLive { get; set; } = true;

    /// <summary>Red while Discord is muted.</summary>
    public bool DiscordMuted { get; set; } = true;

    /// <summary>Orange pulse for something that needs attention (notifications from the local API, later).</summary>
    public bool Attention { get; set; } = true;

    /// <summary>Green when everything is ready.</summary>
    public bool Ready { get; set; } = true;
}

/// <summary>What is going on right now (the inputs of the priority stack).</summary>
public readonly record struct StatusInputs(bool Attention, bool MicMuted, bool ObsLive, bool DiscordMuted, bool Ready);

/// <summary>A colour and effect for the base's edge light, with the reason (for the log and the inspector).</summary>
public readonly record struct StatusLight(byte R, byte G, byte B, LedEffect Effect, string Reason);

/// <summary>
/// The fixed meaning of the colours: <b>orange = attention, red = live / muted, green = ready,
/// the profile's own colour = normal</b>. Never decorative red. The first applicable rule of this priority stack wins; when
/// none applies the result is <c>null</c> and the profile's own edge light is shown again.
/// </summary>
public static class StatusLightResolver
{
    public static readonly StatusLight AttentionLight = new(255, 140, 0, LedEffect.Breathing, "attention");
    public static readonly StatusLight LiveLight = new(255, 0, 0, LedEffect.Static, "live / muted");
    public static readonly StatusLight ReadyLight = new(0, 200, 80, LedEffect.Static, "ready");

    public static StatusLight? Resolve(StatusInputs inputs, StatusLightSettings settings)
    {
        if (!settings.Enabled) return null;

        if (settings.Attention && inputs.Attention) return AttentionLight;

        bool live = (settings.MicMuted && inputs.MicMuted) ||
                    (settings.ObsLive && inputs.ObsLive) ||
                    (settings.DiscordMuted && inputs.DiscordMuted);
        if (live) return LiveLight with { Reason = LiveReason(inputs, settings) };

        if (settings.Ready && inputs.Ready) return ReadyLight;
        return null;
    }

    private static string LiveReason(StatusInputs i, StatusLightSettings s) =>
        s.ObsLive && i.ObsLive ? "OBS is live" :
        s.MicMuted && i.MicMuted ? "microphone muted" : "Discord muted";
}

/// <summary>
/// Watches the sources (microphone mute, OBS streaming / recording, Discord mute) and reports the light the base should show.
/// Nothing runs while the feature is off; the microphone is polled every 2 s only while its reaction is on.
/// </summary>
public sealed class DeckStatusLightService : IDisposable
{
    private readonly IObsService? _obs;
    private readonly IDiscordService? _discord;
    private readonly IAudioService? _audio;
    private readonly Func<StatusLightSettings> _settings;
    private readonly object _lock = new();
    private Timer? _micTimer;
    private bool _attention;
    private bool _started;
    private StatusLight? _current;

    public DeckStatusLightService(IObsService? obs, IDiscordService? discord, IAudioService? audio, Func<StatusLightSettings> settings)
    {
        _obs = obs;
        _discord = discord;
        _audio = audio;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>Raised when the light to show changed; <c>null</c> = show the profile's own edge light again.</summary>
    public event Action<StatusLight?>? Changed;

    public StatusLight? Current
    {
        get { lock (_lock) { return _current; } }
    }

    /// <summary>Something needs attention (orange pulse) – set by whoever knows, e.g. the local API.</summary>
    public bool Attention
    {
        get => _attention;
        set { _attention = value; Refresh(); }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_started) return;
            _started = true;
        }

        if (_obs != null) { _obs.StreamStateChanged += OnChange; _obs.RecordStateChanged += OnChange; }
        if (_discord != null) _discord.VoiceStateChanged += OnVoice;
        Refresh();
    }

    /// <summary>Re-reads the settings and the sources (call after the user changed a reaction).</summary>
    public void Refresh()
    {
        var settings = _settings();
        bool pollMic = settings.Enabled && settings.MicMuted && _audio != null;

        lock (_lock)
        {
            if (pollMic && _micTimer == null) _micTimer = new Timer(_ => Evaluate(), null, 2000, 2000);
            if (!pollMic && _micTimer != null) { _micTimer.Dispose(); _micTimer = null; }
        }

        Evaluate();
    }

    private void OnChange(bool _) => Evaluate();
    private void OnVoice(bool muted, bool deafened) => Evaluate();

    private void Evaluate()
    {
        var settings = _settings();
        StatusLight? light = null;
        if (settings.Enabled)
        {
            bool mic = settings.MicMuted && Safe(() => _audio?.IsMasterRecordingMuted() ?? false);
            bool obs = settings.ObsLive && Safe(() => (_obs?.IsStreaming ?? false) || (_obs?.IsRecording ?? false));
            bool discord = settings.DiscordMuted && Safe(() => _discord?.IsMuted ?? false);
            light = StatusLightResolver.Resolve(new StatusInputs(_attention, mic, obs, discord, Ready: false), settings);
        }

        lock (_lock)
        {
            if (Equals(_current, light)) return;
            _current = light;
        }

        try { Changed?.Invoke(light); } catch { }
    }

    private static bool Safe(Func<bool> read)
    {
        try { return read(); } catch { return false; }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _micTimer?.Dispose();
            _micTimer = null;
            _started = false;
        }

        if (_obs != null) { _obs.StreamStateChanged -= OnChange; _obs.RecordStateChanged -= OnChange; }
        if (_discord != null) _discord.VoiceStateChanged -= OnVoice;
    }
}

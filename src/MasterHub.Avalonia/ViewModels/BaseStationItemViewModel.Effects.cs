using System;
using System.Collections.Generic;
using System.Linq;
using MasterHub.Core.Devices;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>An entry of the edge-effect list.</summary>
public sealed record LedEffectOption(LedEffect Effect, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Edge light of the base (T-18): the effect and its speed per profile, and the status-light reactions.</summary>
public partial class BaseStationItemViewModel
{
    private LedEffect _edgeEffect = LedEffect.Static;
    private byte _edgeSpeed = 50;
    private bool _statusEnabled;
    private bool _statusMic = true;
    private bool _statusObs = true;
    private bool _statusDiscord = true;

    public Action<BaseStationItemViewModel>? EffectChangedCallback { get; set; }
    public Action<BaseStationItemViewModel>? StatusLightsChangedCallback { get; set; }

    /// <summary>Effects the base understands (static, breathing, strobe, …); no per-LED addressing exists.</summary>
    public static IReadOnlyList<LedEffectOption> EffectOptions => Enum.GetValues<LedEffect>()
        .Select(e => new LedEffectOption(e, LocalizationService.Instance.GetString($"Effect_{e}", e.ToString()))).ToList();

    public LedEffectOption SelectedEdgeEffect
    {
        get => EffectOptions.First(o => o.Effect == _edgeEffect);
        set { if (value != null) EdgeEffect = value.Effect; }
    }

    public LedEffect EdgeEffect
    {
        get => _edgeEffect;
        set
        {
            if (!SetField(ref _edgeEffect, value)) return;
            OnPropertyChanged(nameof(SelectedEdgeEffect));
            OnPropertyChanged(nameof(ShowsEdgeSpeed));
            EffectChangedCallback?.Invoke(this);
        }
    }

    public byte EdgeSpeed
    {
        get => _edgeSpeed;
        set
        {
            byte clamped = Math.Min(value, (byte)100);
            if (!SetField(ref _edgeSpeed, clamped)) return;
            OnPropertyChanged(nameof(EdgeSpeedDisplay));
            EffectChangedCallback?.Invoke(this);
        }
    }

    public string EdgeSpeedDisplay => $"{EdgeSpeed}%";

    /// <summary>The speed only matters for the animated effects.</summary>
    public bool ShowsEdgeSpeed => _edgeEffect is not (LedEffect.Static or LedEffect.Disable);

    public bool StatusLightsEnabled
    {
        get => _statusEnabled;
        set { if (SetField(ref _statusEnabled, value)) StatusLightsChangedCallback?.Invoke(this); }
    }

    public bool StatusMicMuted
    {
        get => _statusMic;
        set { if (SetField(ref _statusMic, value)) StatusLightsChangedCallback?.Invoke(this); }
    }

    public bool StatusObsLive
    {
        get => _statusObs;
        set { if (SetField(ref _statusObs, value)) StatusLightsChangedCallback?.Invoke(this); }
    }

    public bool StatusDiscordMuted
    {
        get => _statusDiscord;
        set { if (SetField(ref _statusDiscord, value)) StatusLightsChangedCallback?.Invoke(this); }
    }

    /// <summary>Loads values without raising the callbacks (a profile or the settings were applied).</summary>
    public void LoadEdge(LedEffect effect, int speed)
    {
        _edgeEffect = effect;
        _edgeSpeed = (byte)Math.Clamp(speed, 0, 100);
        OnPropertyChanged(nameof(EdgeEffect));
        OnPropertyChanged(nameof(SelectedEdgeEffect));
        OnPropertyChanged(nameof(EdgeSpeed));
        OnPropertyChanged(nameof(EdgeSpeedDisplay));
        OnPropertyChanged(nameof(ShowsEdgeSpeed));
    }

    public void LoadStatusLights(StatusLightSettings settings)
    {
        _statusEnabled = settings.Enabled;
        _statusMic = settings.MicMuted;
        _statusObs = settings.ObsLive;
        _statusDiscord = settings.DiscordMuted;
        OnPropertyChanged(nameof(StatusLightsEnabled));
        OnPropertyChanged(nameof(StatusMicMuted));
        OnPropertyChanged(nameof(StatusObsLive));
        OnPropertyChanged(nameof(StatusDiscordMuted));
    }
}

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{

    private int GetGpuIndexByName(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return _systemMonitorService.SelectedGpuIndex;
        var names = _systemMonitorService.AvailableGpuNames;
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], gpuName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return _systemMonitorService.SelectedGpuIndex;
    }

    public void UpdateKeyMonitoringValue(KeyItemViewModel key)
    {
        if (key.AssignedAction == DeckActionType.None) return;

        switch (key.AssignedAction)
        {
            case DeckActionType.LiveValue:
                ApplyLiveReading(key);
                break;
            case DeckActionType.DisplayCpuUsage:
                key.LiveValueText = $"{Math.Round(_systemMonitorService.CpuUsagePercent)}%";
                break;
            case DeckActionType.DisplayRamUsage:
                key.LiveValueText = $"{Math.Round(_systemMonitorService.RamUsagePercent)}%";
                break;
            case DeckActionType.DisplayGpuUsage:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    int gpuIdx = GetGpuIndexByName(key.ActionParameter);
                    key.LiveValueText = $"{Math.Round(_systemMonitorService.GetGpuUsagePercent(gpuIdx))}%";
                }
                else
                {
                    key.LiveValueText = $"{Math.Round(_systemMonitorService.GpuUsagePercent)}%";
                }
                break;
            case DeckActionType.DisplayCpuTemp:
                float cpuTemp = _systemMonitorService.CpuTemperatureCelsius;
                key.LiveValueText = cpuTemp > 0 ? $"{Math.Round(cpuTemp)}°C" : $"{Math.Round(_systemMonitorService.GpuTemperatureCelsius)}°C";
                break;
            case DeckActionType.DisplayGpuTemp:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    int gpuIdx = GetGpuIndexByName(key.ActionParameter);
                    key.LiveValueText = $"{Math.Round(_systemMonitorService.GetGpuTemperatureCelsius(gpuIdx))}°C";
                }
                else
                {
                    key.LiveValueText = $"{Math.Round(_systemMonitorService.GpuTemperatureCelsius)}°C";
                }
                break;
            case DeckActionType.DisplayNetworkSpeed:
                key.LiveValueText = FormatNetworkSpeed(
                    _systemMonitorService.NetworkDownloadBytesPerSec,
                    _systemMonitorService.NetworkDownloadMbps,
                    key.ActionParameter);
                break;
            case DeckActionType.DisplayDiskUsage:
                key.LiveValueText = $"{Math.Round(_systemMonitorService.GetDiskUsagePercent(key.ActionParameter))}%";
                break;
            case DeckActionType.DisplayClock:
                key.LiveValueText = DateTime.Now.ToString("HH:mm");
                break;
            case DeckActionType.DisplayDate:
                key.LiveValueText = DateTime.Now.ToString("dd MMM");
                break;
            case DeckActionType.DisplayFps:
                key.LiveValueText = $"{Math.Round(_systemMonitorService.CurrentFps)} FPS";
                break;
            case DeckActionType.NowPlaying:
                key.LiveValueText = _nowPlayingService?.DisplayTrack ?? "";
                break;
        }
    }

    public static string FormatNetworkSpeed(double bytesPerSec, float fallbackMbps, string? unitPreference)
    {
        bool isBitMode = unitPreference != null && (
            unitPreference.Contains("Mb/s", StringComparison.Ordinal) ||
            unitPreference.Contains("Kb/s", StringComparison.Ordinal) ||
            unitPreference.Contains("Gb/s", StringComparison.Ordinal) ||
            unitPreference.Contains("b/s", StringComparison.Ordinal) ||
            unitPreference.Contains("bit", StringComparison.OrdinalIgnoreCase) ||
            unitPreference.Contains("bps", StringComparison.OrdinalIgnoreCase));

        if (isBitMode)
        {
            double bitsPerSec = (bytesPerSec > 0) ? (bytesPerSec * 8.0) : (fallbackMbps * 1_000_000.0);
            if (bitsPerSec >= 1_000_000_000.0)
                return $"{bitsPerSec / 1_000_000_000.0:0.##} Gb/s";
            if (bitsPerSec >= 1_000_000.0)
                return $"{bitsPerSec / 1_000_000.0:0.#} Mb/s";
            if (bitsPerSec >= 1000.0)
                return $"{bitsPerSec / 1000.0:0.#} Kb/s";
            return $"{bitsPerSec:0} b/s";
        }
        else
        {
            double bytes = (bytesPerSec > 0) ? bytesPerSec : (fallbackMbps * 1_000_000.0 / 8.0);
            if (bytes >= 1024.0 * 1024.0 * 1024.0)
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB/s";
            if (bytes >= 1024.0 * 1024.0)
                return $"{bytes / (1024.0 * 1024.0):0.#} MB/s";
            if (bytes >= 1024.0)
                return $"{bytes / 1024.0:0.#} KB/s";
            return $"{bytes:0} B/s";
        }
    }

    private System.Threading.Timer? _reconnectTimer;

    /// <summary>
    /// Osobny, rzadki timer ponownych połączeń integracji (Discord/OBS/Twitch/Kick). Wcześniej ta logika siedziała w
    /// OnMetricsUpdated, czyli w tiku monitora systemu - wymuszało to próbkowanie metryk co sekundę nawet bez żadnego
    /// klawisza pokazującego dane. Teraz monitor można wyłączyć, a reconnect działa niezależnie (co 15 s, nie w standby).
    /// </summary>
    private void StartReconnectTimer()
    {
        _reconnectTimer ??= new System.Threading.Timer(_ => ReconnectIntegrationsTick(), null,
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    private void ReconnectIntegrationsTick()
    {
        if (_disposed || _deviceService.IsStandby) return;
        if (!_discordService.IsConnected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    bool isDiscordRunning = MasterHub.Core.SystemControl.ProcessHelper.IsAnyProcessRunning("Discord", "DiscordCanary", "DiscordPTB");

                    if (isDiscordRunning)
                    {
                        var dc = _profileService.GetDiscordSettings();
                        await _discordService.ConnectAsync(dc.ClientId, dc.AccessToken);
                    }
                }
                catch { }
            });
        }
        if (!_obsService.IsConnected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var obs = _profileService.GetObsSettings();
                    if (obs.AutoConnect)
                    {
                        bool isLocal = string.IsNullOrWhiteSpace(obs.Host) ||
                                       obs.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                       obs.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

                        bool isRunning = !isLocal || MasterHub.Core.SystemControl.ProcessHelper.IsAnyProcessRunning("obs64", "obs32", "obs");

                        if (isRunning)
                        {
                            await _obsService.ConnectAsync(obs.Host, obs.Port, obs.Password);
                        }
                    }
                }
                catch { }
            });
        }
        if (!_twitchService.IsConnected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var twitch = _profileService.GetTwitchSettings();
                    if (twitch.AutoConnect && !string.IsNullOrWhiteSpace(twitch.Channel))
                    {
                        await _twitchService.ConnectAsync(twitch.Channel, twitch.OAuthToken, twitch.BotUsername, twitch.ClientId);
                    }
                }
                catch { }
            });
        }
        if (!_kickService.IsConnected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var kick = _profileService.GetKickSettings();
                    if (kick.AutoConnect && !string.IsNullOrWhiteSpace(kick.Channel))
                    {
                        await _kickService.ConnectAsync(kick.Channel, kick.AuthToken, kick.BotUsername);
                    }
                }
                catch { }
            });
        }
    }

    private void OnMetricsUpdated()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.None) continue;

                string? prev = key.LiveValueText;
                UpdateKeyMonitoringValue(key);

                if (key.LiveValueText != prev)
                {
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    /// <summary>Grupy metryk potrzebne dla danej akcji klawisza (None = akcja nie korzysta z monitora).</summary>
    internal static MonitorMetrics MetricsFor(DeckActionType action) => action switch
    {
        DeckActionType.DisplayCpuUsage => MonitorMetrics.Cpu,
        DeckActionType.DisplayRamUsage => MonitorMetrics.Ram,
        DeckActionType.DisplayGpuUsage => MonitorMetrics.Gpu,
        DeckActionType.DisplayGpuTemp => MonitorMetrics.Gpu,
        DeckActionType.DisplayCpuTemp => MonitorMetrics.CpuTemperature | MonitorMetrics.Gpu,
        DeckActionType.DisplayNetworkSpeed => MonitorMetrics.Network,
        DeckActionType.DisplayDiskUsage => MonitorMetrics.Disk,
        DeckActionType.DisplayFps => MonitorMetrics.Fps | MonitorMetrics.Gpu,
        _ => MonitorMetrics.None
    };

    /// <summary>
    /// Włącza kosztowne próbkowanie tylko dla metryk faktycznie wyświetlanych na bieżącym widoku decka.
    /// Brak klawiszy z danymi i zegarem = monitor wyłączony (0 wybudzeń); sam zegar/data = tani tik co 2 s bez próbkowania.
    /// Wywoływane po zmianie widoku (strona/folder/profil) i po zmianie akcji klawisza.
    /// </summary>
    public void UpdateMonitoringDemand()
    {
        UpdateLiveValueDemand();
        RefreshTimerKeys(render: !_isApplyingProfile);   // T-13: a view that shows a timer shows where it stands
        EnsureTimerTick();
        var needed = MonitorMetrics.None;
        bool needsClockTick = false;
        bool needsNowPlaying = false;
        foreach (var key in Keys)
        {
            needed |= MetricsFor(key.AssignedAction);
            if (key.AssignedAction is DeckActionType.DisplayClock or DeckActionType.DisplayDate) needsClockTick = true;
            if (key.AssignedAction == DeckActionType.NowPlaying) needsNowPlaying = true;
        }

        // T-12: reguły "Gdy…" i tokeny w etykietach też potrzebują metryk / odtwarzacza; timer reguł działa tylko, gdy jakiś klawisz ich używa
        var look = CollectLookDemand();
        needed |= look.Metrics;
        if (look.NowPlaying) needsNowPlaying = true;
        UpdateLookTimer(look);
        if (look.Any && !_isApplyingProfile) EvaluateLooks();   // pierwszy wygląd od razu, nie dopiero po 500 ms

        // Odpytywanie Windows Media (GSMTC) co sekundę tylko wtedy, gdy któryś klawisz pokazuje "Teraz odtwarzane".
        if (_nowPlayingService.IsEnabled != needsNowPlaying) _nowPlayingService.IsEnabled = needsNowPlaying;

        _systemMonitorService.RequiredMetrics = needed;
        if (needed == MonitorMetrics.None && !needsClockTick)
        {
            _systemMonitorService.IsEnabled = false;
            return;
        }

        int interval = needed == MonitorMetrics.None ? 2000 : 1000;
        if (_systemMonitorService.RefreshIntervalMs != interval) _systemMonitorService.RefreshIntervalMs = interval;
        if (!_systemMonitorService.IsEnabled) _systemMonitorService.IsEnabled = true;
    }
}

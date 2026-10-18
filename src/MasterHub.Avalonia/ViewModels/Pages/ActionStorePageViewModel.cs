using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Teams;

using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Pages;

public record IntegrationStatusItem(string Name, string Description, string Icon, string Status, bool IsOnline);

public partial class ActionStorePageViewModel : ObservableObject
{
    private readonly IObsService? _obsService;
    private readonly IDiscordService? _discordService;
    private readonly ITeamsService? _teamsService;

    public ObservableCollection<IntegrationStatusItem> Integrations { get; } = new();

    public ActionStorePageViewModel(
        IObsService? obsService = null,
        IDiscordService? discordService = null,
        ITeamsService? teamsService = null)
    {
        _obsService = obsService;
        _discordService = discordService;
        _teamsService = teamsService;

        if (_obsService != null)
        {
            _obsService.ConnectionStateChanged += _ => Dispatcher.UIThread.Post(RefreshIntegrations);
        }
        if (_discordService != null)
        {
            _discordService.ConnectionStateChanged += _ => Dispatcher.UIThread.Post(RefreshIntegrations);
        }

        LocalizationService.Instance.LanguageChanged += _ => RefreshIntegrations();
        RefreshIntegrations();
    }

    public void RefreshIntegrations()
    {
        Integrations.Clear();
        var loc = LocalizationService.Instance;

        bool obsOnline = _obsService?.IsConnected ?? false;
        Integrations.Add(new IntegrationStatusItem(
            "OBS Studio",
            loc.GetString("Store_ObsDesc", "Transmisja na żywo, nagrywanie, wirtualna kamera i przełączanie scen."),
            "📡",
            obsOnline ? loc.GetString("Store_ObsOnline", "Połączono (WebSocket v5)") : loc.GetString("Store_ObsOffline", "Gotowy do połączenia"),
            obsOnline));

        bool discordOnline = _discordService?.IsConnected ?? false;
        Integrations.Add(new IntegrationStatusItem(
            "Discord",
            loc.GetString("Store_DiscordDesc", "Wyciszanie mikrofonu, podsłuch, kamera, stream oraz Push-To-Talk."),
            "🎙️",
            discordOnline ? loc.GetString("Store_DiscordOnline", "Aktywny (Local IPC)") : loc.GetString("Store_DiscordOffline", "Oczekiwanie na klienta"),
            discordOnline));

        bool teamsOnline = _teamsService?.IsTeamsRunning ?? false;
        Integrations.Add(new IntegrationStatusItem(
            "Microsoft Teams",
            loc.GetString("Store_TeamsDesc", "Szybkie wyciszanie, podnoszenie ręki i sterowanie kamerą na spotkaniach."),
            "💼",
            teamsOnline ? loc.GetString("Store_TeamsOnline", "Wykryto proces Teams") : loc.GetString("Store_TeamsOffline", "Nieuruchomiony"),
            teamsOnline));

        Integrations.Add(new IntegrationStatusItem(
            "System & Media",
            loc.GetString("Store_SysMediaDesc", "Monitor CPU/RAM/GPU, głośność Windows, multimedialny odtwarzacz audio."),
            "🖥️",
            loc.GetString("Store_SysMediaActive", "Aktywny (System API)"),
            true));

        Integrations.Add(new IntegrationStatusItem(
            "Razer Synapse & Wooting",
            loc.GetString("Store_HardwareDesc", "Szybkie przełączanie profili sprzętowych, cykle DPI i polling rate."),
            "⚡",
            loc.GetString("Store_HardwareReady", "Gotowy (HID SDK)"),
            true));
    }
}

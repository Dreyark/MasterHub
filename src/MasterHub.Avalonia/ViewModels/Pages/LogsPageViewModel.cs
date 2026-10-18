using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Pages;

public partial class LogsPageViewModel : ObservableObject, IDisposable
{
    private readonly IMasterHubDeviceService _deviceService;
    private readonly MasterHub.Core.Integrations.Obs.IObsService? _obsService;
    private bool _disposed;

    public ObservableCollection<LogEntryItemViewModel> LogEntries { get; } = new();

    public LogsPageViewModel(IMasterHubDeviceService deviceService, MasterHub.Core.Integrations.Obs.IObsService? obsService = null)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _obsService = obsService;

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        AddLog(() => LocalizationService.Instance.GetString("Log_Init", "Inicjalizacja podsystemu dziennika zdarzeń MasterHub."));
        AddLog(() => string.Format(LocalizationService.Instance.GetString("Log_KeypadConnection", "Połączenie klawiatury LCD: {0}"), _deviceService.IsKeypadConnected ? "ONLINE" : "OFFLINE"));
        AddLog(() => string.Format(LocalizationService.Instance.GetString("Log_SliderConnection", "Połączenie suwaków audio: {0}"), _deviceService.IsSliderConnected ? "ONLINE" : "OFFLINE"));
        AddLog(() => string.Format(LocalizationService.Instance.GetString("Log_RollerConnection", "Połączenie rolek obrotowych: {0}"), _deviceService.IsRollerConnected ? "ONLINE" : "OFFLINE"));
        AddLog(() => string.Format(LocalizationService.Instance.GetString("Log_BaseConnection", "Połączenie stacji dokującej: {0}"), _deviceService.IsBaseConnected ? "ONLINE" : "OFFLINE"));

        _deviceService.KeyStateChanged += OnKeyStateChanged;
        _deviceService.SliderValueChanged += OnSliderValueChanged;
        _deviceService.RollerRotated += OnRollerRotated;
        _deviceService.StandbyStateChanged += OnStandbyStateChanged;
        _deviceService.DevicesUpdated += OnDevicesUpdated;

        if (_obsService != null)
        {
            _obsService.ConnectionStateChanged += OnObsConnectionChanged;
            _obsService.StreamStateChanged += OnObsStreamStateChanged;
            _obsService.RecordStateChanged += OnObsRecordStateChanged;
            _obsService.ReplayBufferSaved += OnObsReplayBufferSaved;
        }
    }

    private void OnLanguageChanged(string _)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var entry in LogEntries)
            {
                entry.Refresh();
            }
        });
    }

    private void AddLog(Func<string> messageProvider)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogEntries.Insert(0, new LogEntryItemViewModel(DateTime.Now, messageProvider));
            if (LogEntries.Count > 150)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        });
    }

    private void OnKeyStateChanged(int keyIndex, bool isPressed)
    {
        AddLog(() =>
        {
            var loc = LocalizationService.Instance;
            string state = isPressed ? loc.GetString("Log_StateDown", "WCIŚNIĘTY (DOWN)") : loc.GetString("Log_StateUp", "ZWOLNIONY (UP)");
            return string.Format(loc.GetString("Log_KeyState", "[Klawiatura LCD] Klawisz #{0} -> {1}"), keyIndex + 1, state);
        });
    }

    private void OnSliderValueChanged(int sliderIndex, byte raw, int percent)
    {
        AddLog(() => string.Format(LocalizationService.Instance.GetString("Log_SliderValue", "[Suwak Audio] Suwak #{0} -> Pozycja: {1}% (Raw: {2})"), sliderIndex + 1, percent, raw));
    }

    private void OnRollerRotated(int rollerIndex, int direction)
    {
        AddLog(() =>
        {
            var loc = LocalizationService.Instance;
            string dir = direction > 0 ? loc.GetString("Log_DirUp", "Góra ▲ (+1)") : loc.GetString("Log_DirDown", "Dół ▼ (-1)");
            return string.Format(loc.GetString("Log_RollerRotated", "[Rolka Enkodera] Rolka #{0} -> Obrót: {1}"), rollerIndex + 1, dir);
        });
    }

    private void OnStandbyStateChanged(bool isStandby)
    {
        AddLog(() =>
        {
            var loc = LocalizationService.Instance;
            string state = isStandby ? loc.GetString("Log_StandbyOn", "WŁĄCZONY (Standby)") : loc.GetString("Log_StandbyOff", "WYBUDZONY (Active)");
            return string.Format(loc.GetString("Log_StandbyState", "[Zasilanie] Tryb uśpienia: {0}"), state);
        });
    }

    private void OnDevicesUpdated()
    {
        AddLog(() => LocalizationService.Instance.GetString("Log_DevicesUpdated", "[Magistrala USB] Zaktualizowano listę wykrytych urządzeń MasterHub."));
    }

    private void OnObsConnectionChanged(bool isConnected)
    {
        AddLog(() =>
        {
            var loc = LocalizationService.Instance;
            if (isConnected)
            {
                string ver = _obsService?.ObsWebSocketVersion ?? "v5";
                return string.Format(loc.GetString("Log_ObsConnected", "[OBS Studio] Połączono z serwerem WebSocket v5 (Wersja: {0})"), ver);
            }
            return loc.GetString("Log_ObsDisconnected", "[OBS Studio] Rozłączono sesję WebSocket.");
        });
    }

    private void OnObsStreamStateChanged(bool isStreaming)
    {
        AddLog(() =>
        {
            var loc = LocalizationService.Instance;
            string state = isStreaming ? loc.GetString("Log_ObsStreamStarted", "NADAWANIE ROZPOCZĘTE (LIVE)") : loc.GetString("Log_ObsStreamStopped", "ZATRZYMANY (OFFLINE)");
            return string.Format(loc.GetString("Log_ObsStreamState", "[OBS Studio] Transmisja na żywo: {0}"), state);
        });
    }

    private void OnObsRecordStateChanged(bool isRecording)
    {
        AddLog(() =>
        {
            var loc = LocalizationService.Instance;
            string state = isRecording ? loc.GetString("Log_ObsRecordStarted", "NAGRYWANIE W TOKU (REC)") : loc.GetString("Log_ObsRecordStopped", "ZATRZYMANO (STOP)");
            return string.Format(loc.GetString("Log_ObsRecordState", "[OBS Studio] Nagrywanie wideo: {0}"), state);
        });
    }

    private void OnObsReplayBufferSaved(string filePath)
    {
        AddLog(() => string.Format(LocalizationService.Instance.GetString("Log_ObsReplaySaved", "[OBS Studio] Zapisano bufor powtórki: {0}"), filePath));
    }

    [RelayCommand]
    public void ClearLogs()
    {
        LogEntries.Clear();
        AddLog(() => LocalizationService.Instance.GetString("Log_Cleared", "Dziennik wyczyszczony."));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        _deviceService.KeyStateChanged -= OnKeyStateChanged;
        _deviceService.SliderValueChanged -= OnSliderValueChanged;
        _deviceService.RollerRotated -= OnRollerRotated;
        _deviceService.StandbyStateChanged -= OnStandbyStateChanged;
        _deviceService.DevicesUpdated -= OnDevicesUpdated;

        if (_obsService != null)
        {
            _obsService.ConnectionStateChanged -= OnObsConnectionChanged;
            _obsService.StreamStateChanged -= OnObsStreamStateChanged;
            _obsService.RecordStateChanged -= OnObsRecordStateChanged;
            _obsService.ReplayBufferSaved -= OnObsReplayBufferSaved;
        }
    }
}

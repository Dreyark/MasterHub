using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using MasterHub.Core.Models;
using MasterHub.Core.Native;
using MasterHub.Core.Rendering;

namespace MasterHub.Core.Devices;

public class MasterHubManager : IMasterHubManager
{
    private MasterHubDeviceHandle? _hBase;
    private MasterHubDeviceHandle? _hKeypad;
    private MasterHubDeviceHandle? _hSlider;
    private MasterHubDeviceHandle? _hRoller;

    public bool IsKeypadConnected => _hKeypad != null && _hKeypad.IsValid;
    public bool IsSliderConnected => _hSlider != null && _hSlider.IsValid;
    public bool IsRollerConnected => _hRoller != null && _hRoller.IsValid;
    public bool IsBaseConnected => _hBase != null && _hBase.IsValid;

    private bool _isStandby = false;
    public bool IsStandby => _isStandby;

    private readonly bool[] _keyStates = new bool[15];
    private readonly byte[] _sliderValues = new byte[5];
    private readonly byte[] _lastSliderValues = new byte[5];

    private Thread? _pollThread;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _hotPlugCts;
    private bool _disposed = false;

    // Events
    public event Action<int, bool>? KeyStateChanged; // (keyIndex 0..14, isPressed)
    public event Action<int, byte, int>? SliderValueChanged; // (sliderIndex 0..4, raw 0..255, percent 0..100)
    public event Action<int, int>? RollerRotated; // (rollerIndex 0=Left, 1=Right; direction +1=Up, -1=Down)
    public event Action<string>? LogMessage;
    public event Action<List<DeviceInfo>>? DevicesDiscovered;
    public event Action<bool>? StandbyStateChanged;

    public List<DeviceInfo> ConnectedDevices { get; } = new();
    public IReadOnlyList<bool> CurrentKeyStates => _keyStates;
    public IReadOnlyList<byte> CurrentSliderValues => _sliderValues;
    public Func<int, bool, byte[]>? CustomKeyImageProvider { get; set; }

    public void Start()
    {
        Log("Inicjalizacja środowiska MasterHub (czysty sterownik Open Source USB HID)...");

        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch { }

        CheckOfficialSoftwareRunning();
        DiscoverAndConnect();

        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "MasterHub-PollThread",
            Priority = ThreadPriority.AboveNormal
        };
        _pollThread.Start();

        _hotPlugCts = new CancellationTokenSource();
        Task.Run(() => HotPlugBackgroundLoop(_hotPlugCts.Token));
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Log("Wykryto uśpienie systemu Windows -> przełączanie urządzeń w tryb Standby");
            EnterStandby();
            DisconnectAll();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Log("Wykryto wybudzenie systemu Windows -> oczekiwanie na gotowość kontrolera USB...");
            Task.Run(async () =>
            {
                await Task.Delay(1500);
                Log("Wznawianie połączeń MasterHub po wybudzeniu systemu...");
                DiscoverAndConnect();
            });
        }
    }

    private void CheckOfficialSoftwareRunning()
    {
        if (Process.GetProcessesByName("MasterHUB").Length > 0)
        {
            Log("⚠️ UWAGA: Oficjalny proces Cooler Master MasterHUB.exe jest uruchomiony w tle!");
            Log("   Może on blokować wyłączny dostęp USB do modułów.");
            Log("   Kliknij czerwony przycisk 'Zamknij oficjalny MasterHUB' u góry ekranu, aby go zamknąć.");
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    public void DiscoverAndConnect()
    {
        // Bezpieczne rozłączenie poprzednich sesji
        DisconnectAll();

        _isStandby = false;
        StandbyStateChanged?.Invoke(false);
        ConnectedDevices.Clear();
        var discovered = ScanUsbDevices();

        // 1. Zawsze najpierw podłączamy Stację Bazową (Base Station) - zasila dok i moduły!
        var baseDev = discovered.FirstOrDefault(d => d.ModuleType == MasterHubModuleType.BaseStation);
        if (baseDev != null)
        {
            ConnectBase(baseDev);
        }

        // 2. Podłączamy Keypad, Slider oraz Rolki
        foreach (var dev in discovered)
        {
            if (dev.ModuleType == MasterHubModuleType.BaseStation) continue;

            switch (dev.ModuleType)
            {
                case MasterHubModuleType.KeyDeck15:
                    ConnectKeypad(dev);
                    break;
                case MasterHubModuleType.Fader5:
                    ConnectSlider(dev);
                    break;
                case MasterHubModuleType.RollerDial:
                    ConnectRoller(dev);
                    break;
                default:
                    Log($"Wykryto moduł: {dev.Name} [SN: {dev.SerialNumber}]");
                    dev.IsConnected = true;
                    AddOrUpdateConnectedDevice(dev);
                    break;
            }
        }

        DevicesDiscovered?.Invoke(new List<DeviceInfo>(ConnectedDevices));
        WakeUp();
    }

    private List<DeviceInfo> ScanUsbDevices()
    {
        var list = MasterHubUsbTransport.ScanDevices();

        // Sprawdź również ewentualny monitor IPS (nie-HID)
        if (!list.Any(d => d.ModuleType == MasterHubModuleType.ScreenDisplay))
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"SELECT DeviceID, Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\VID_2516%' AND Present = TRUE");
                foreach (ManagementBaseObject obj in searcher.Get())
                {
                    string deviceId = obj["DeviceID"]?.ToString() ?? "";
                    var match = Regex.Match(deviceId, @"USB\\VID_2516&PID_([0-9A-Fa-f]{4})\\([^\\]+)");
                    if (match.Success)
                    {
                        ushort pid = Convert.ToUInt16(match.Groups[1].Value, 16);
                        if (pid == 0x131E || pid == 0x1312)
                        {
                            string serial = match.Groups[2].Value;
                            if (!list.Any(d => d.SerialNumber == serial))
                            {
                                list.Add(new DeviceInfo
                                {
                                    VendorId = 0x2516,
                                    ProductId = pid,
                                    SerialNumber = serial,
                                    Name = "MasterHub Display (Ekran IPS)",
                                    ModuleType = MasterHubModuleType.ScreenDisplay
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Błąd skanowania USB WMI: {ex.Message}");
            }
        }

        return list;
    }

    private void AddOrUpdateConnectedDevice(DeviceInfo dev)
    {
        var existing = ConnectedDevices.FirstOrDefault(d => d.SerialNumber == dev.SerialNumber || d.ModuleType == dev.ModuleType);
        if (existing != null)
        {
            existing.IsConnected = dev.IsConnected;
            existing.Name = dev.Name;
            existing.SerialNumber = dev.SerialNumber;
            existing.ProductId = dev.ProductId;
            existing.VendorId = dev.VendorId;
        }
        else
        {
            ConnectedDevices.Add(dev);
        }
    }

    private void ConnectBase(DeviceInfo dev)
    {
        Log($"Łączenie ze Stacją Bazową ({dev.SerialNumber})...");
        _hBase = MasterHubUsbTransport.ConnectDevice(dev.VendorId, dev.ProductId, dev.SerialNumber);
        if (_hBase != null && _hBase.IsValid)
        {
            dev.IsConnected = true;
            AddOrUpdateConnectedDevice(dev);
            Log($"Połączono ze Stacją Bazową! (HID: {_hBase.DevicePath})");
        }
        else
        {
            dev.IsConnected = false;
            AddOrUpdateConnectedDevice(dev);
            Log($"Nie udało się połączyć ze Stacją Bazową.");
        }
    }

    private void ConnectKeypad(DeviceInfo dev)
    {
        Log($"Łączenie z 15-Keypad ({dev.SerialNumber})...");
        _hKeypad = MasterHubUsbTransport.ConnectDevice(dev.VendorId, dev.ProductId, dev.SerialNumber);
        if (_hKeypad != null && _hKeypad.IsValid)
        {
            dev.IsConnected = true;
            AddOrUpdateConnectedDevice(dev);
            Log($"Połączono pomyślnie z 15-Keypad! (HID: {_hKeypad.DevicePath})");

            // 1. Aktywacja trybu programowego (Software Mode)
            int rSw = _hKeypad.SetSwMode(true);
            Log($"Tryb Software Mode włączony (wynik: {rSw}).");

            // 2. Wybudzenie podświetlenia ekraników LCD na 100%
            int rBl = _hKeypad.AdjustBacklight(100);
            Log($"Podświetlenie LCD ustawione na 100% (wynik: {rBl}).");

            // 3. Wyświetlenie grafik na 15 przyciskach
            InitializeKeypadDisplay();
        }
        else
        {
            dev.IsConnected = false;
            AddOrUpdateConnectedDevice(dev);
            Log($"Nie udało się połączyć z Keypad.");
            if (Process.GetProcessesByName("MasterHUB").Length > 0)
            {
                Log("   -> Prawdopodobna przyczyna: oficjalna aplikacja MasterHUB blokuje dostęp.");
            }
        }
    }

    private void ConnectSlider(DeviceInfo dev)
    {
        Log($"Łączenie ze Sliderem ({dev.SerialNumber})...");
        _hSlider = MasterHubUsbTransport.ConnectDevice(dev.VendorId, dev.ProductId, dev.SerialNumber);
        if (_hSlider != null && _hSlider.IsValid)
        {
            dev.IsConnected = true;
            AddOrUpdateConnectedDevice(dev);
            Log($"Połączono pomyślnie z modułem 5 suwaków! (HID: {_hSlider.DevicePath})");

            // Włącz podświetlenie suwaka w trybie Static (1)
            _hSlider.AdjustLedMode(1, 0xFF, 1, false);
            _hSlider.AdjustLedBrightness(1, 0xFF, _currentBrightness);

            // Pobierz początkowe wartości
            int rGet = _hSlider.GetSliderValues(_sliderValues);
            Array.Copy(_sliderValues, _lastSliderValues, 5);
            for (int i = 0; i < 5; i++)
            {
                int pct = Math.Clamp((int)_sliderValues[i], 0, 100);
                SliderValueChanged?.Invoke(i, _sliderValues[i], pct);
            }
            Log($"Początkowe wartości suwaków: [{string.Join(", ", _sliderValues)}]");
        }
        else
        {
            dev.IsConnected = false;
            AddOrUpdateConnectedDevice(dev);
            Log($"Nie udało się połączyć ze Sliderem.");
        }
    }

    private void ConnectRoller(DeviceInfo dev)
    {
        Log($"Łączenie z modułem 2 Rolek ({dev.SerialNumber})...");
        _hRoller = MasterHubUsbTransport.ConnectDevice(dev.VendorId, dev.ProductId, dev.SerialNumber);
        if (_hRoller != null && _hRoller.IsValid)
        {
            dev.IsConnected = true;
            AddOrUpdateConnectedDevice(dev);
            Log($"Połączono pomyślnie z modułem 2 Rolek! (HID: {_hRoller.DevicePath})");

            // Włącz podświetlenie rolek (jeśli obecne)
            _hRoller.AdjustLedBrightness(2, 0xFF, _currentBrightness);
        }
        else
        {
            dev.IsConnected = false;
            AddOrUpdateConnectedDevice(dev);
            Log($"Nie udało się połączyć z modułem 2 Rolek.");
        }
    }

    private byte _currentBrightness = 100;
    public byte CurrentBrightness => _currentBrightness;

    public void EnterStandby()
    {
        _isStandby = true;
        StandbyStateChanged?.Invoke(true);
        Log("Przełączanie modułów w tryb Standby (uśpienie)...");

        // 1. Moduł 15 klawiszy LCD
        if (_hKeypad != null && _hKeypad.IsValid)
        {
            try
            {
                _hKeypad.AdjustBacklight(0);
                for (byte i = 0; i < 15; i++)
                {
                    _hKeypad.DisplayColorOnButton(i, 0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Log($"Błąd uśpienia Keypad: {ex.Message}");
            }
        }

        // 2. Stacja bazowa (Base Plate)
        if (_hBase != null && _hBase.IsValid)
        {
            try
            {
                _hBase.AdjustLedBrightness(0, 0, 0);
                _hBase.AdjustLedBrightness(0, 0xFF, 0);
                _hBase.AdjustLedMode(0, 0, 0, false);
                _hBase.AdjustLedColor(0, 0, 0, 0, 0, false);
                _hBase.AdjustLedColor(0, 0xFF, 0, 0, 0, false);
            }
            catch { }
        }

        // 3. Moduł suwaków (Sliders)
        if (_hSlider != null && _hSlider.IsValid)
        {
            try
            {
                _hSlider.AdjustLedBrightness(1, 0, 0);
                _hSlider.AdjustLedBrightness(1, 0xFF, 0);
                _hSlider.AdjustLedBrightness(0, 0xFF, 0);
                _hSlider.AdjustLedMode(1, 0xFF, 0, false);
                _hSlider.AdjustLedColor(1, 0xFF, 0, 0, 0, false);
                for (byte s = 0; s < 5; s++)
                {
                    _hSlider.AdjustLedBrightness(1, s, 0);
                    _hSlider.AdjustLedColor(1, s, 0, 0, 0, false);
                }
            }
            catch { }
        }

        // 4. Moduł rolek (Rollers)
        if (_hRoller != null && _hRoller.IsValid)
        {
            try
            {
                _hRoller.AdjustLedBrightness(2, 0, 0);
                _hRoller.AdjustLedBrightness(2, 0xFF, 0);
                _hRoller.AdjustLedBrightness(0, 0xFF, 0);
                _hRoller.AdjustLedMode(2, 0xFF, 0, false);
                _hRoller.AdjustLedColor(2, 0xFF, 0, 0, 0, false);
                for (byte r = 0; r < 2; r++)
                {
                    _hRoller.AdjustLedBrightness(2, r, 0);
                    _hRoller.AdjustLedColor(2, r, 0, 0, 0, false);
                }
            }
            catch { }
        }
    }

    public void WakeUp()
    {
        _isStandby = false;
        Log("Wybudzanie modułów MasterHub ze stanu Standby...");

        if (_hKeypad == null && _hSlider == null && _hBase == null && _hRoller == null)
        {
            DiscoverAndConnect();
            return;
        }

        // 1. Moduł 15 klawiszy LCD
        if (_hKeypad != null && _hKeypad.IsValid)
        {
            try
            {
                _hKeypad.SetSwMode(true);
                _hKeypad.AdjustBacklight(_currentBrightness);
                InitializeKeypadDisplay();
            }
            catch (Exception ex)
            {
                Log($"Błąd wybudzania Keypad: {ex.Message}");
            }
        }

        // 2. Stacja bazowa
        if (_hBase != null && _hBase.IsValid)
        {
            try 
            { 
                _hBase.AdjustLedMode(0, 0, 1, false);
                _hBase.AdjustLedBrightness(0, 0, _currentBrightness); 
            } catch { }
        }

        // 3. Moduł suwaków
        if (_hSlider != null && _hSlider.IsValid)
        {
            try 
            { 
                _hSlider.AdjustLedMode(1, 0xFF, 1, false);
                _hSlider.AdjustLedBrightness(1, 0xFF, _currentBrightness); 
            } catch { }
        }

        // 4. Moduł rolek
        if (_hRoller != null && _hRoller.IsValid)
        {
            try 
            { 
                _hRoller.AdjustLedMode(2, 0xFF, 1, false);
                _hRoller.AdjustLedBrightness(2, 0xFF, _currentBrightness); 
            } catch { }
        }

        StandbyStateChanged?.Invoke(false);
    }

    public void SetBacklight(byte brightness)
    {
        _currentBrightness = brightness;
        if (_hKeypad != null && _hKeypad.IsValid)
        {
            try { _hKeypad.AdjustBacklight(brightness); } catch { }
        }

        if (_hBase != null && _hBase.IsValid)
        {
            try { _hBase.AdjustLedBrightness(0, 0, brightness); } catch { }
        }

        if (_hSlider != null && _hSlider.IsValid)
        {
            try { _hSlider.AdjustLedBrightness(1, 0xFF, brightness); } catch { }
        }

        if (_hRoller != null && _hRoller.IsValid)
        {
            try { _hRoller.AdjustLedBrightness(2, 0xFF, brightness); } catch { }
        }

        Log($"Jasność podświetlenia i diod LED ustawiona na: {brightness}%");
    }

    public void SetLedColor(byte r, byte g, byte b)
    {
        if (_hBase != null && _hBase.IsValid)
        {
            try 
            { 
                _hBase.AdjustLedMode(0, 0, 1, false);
                _hBase.AdjustLedColor(0, 0, r, g, b, false); 
            } catch { }
        }

        if (_hSlider != null && _hSlider.IsValid)
        {
            try 
            { 
                _hSlider.AdjustLedMode(1, 0xFF, 1, false);
                _hSlider.AdjustLedColor(1, 0xFF, r, g, b, false); 
            } catch { }
        }

        if (_hRoller != null && _hRoller.IsValid)
        {
            try 
            { 
                _hRoller.AdjustLedMode(2, 0xFF, 1, false);
                _hRoller.AdjustLedColor(2, 0xFF, r, g, b, false); 
            } catch { }
        }

        Log($"Kolor podświetlenia LED modułów ustawiony na: RGB({r}, {g}, {b})");
    }

    public void SetSliderLedColor(int sliderIndex, byte r, byte g, byte b)
    {
        if (_hSlider != null && _hSlider.IsValid)
        {
            try
            {
                byte ledIndex = (sliderIndex >= 0 && sliderIndex < 5) ? (byte)sliderIndex : (byte)0xFF;
                _hSlider.AdjustLedMode(1, ledIndex, 1, false);
                int ret = _hSlider.AdjustLedColor(1, ledIndex, r, g, b, false);
                string label = (sliderIndex >= 0 && sliderIndex < 5) ? $"SUWAKA #{sliderIndex + 1}" : "WSZYSTKICH SUWAKÓW";
                Log($"[LED {label}] Ustawiono sprzętowy kolor RGB({r}, {g}, {b}) (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD LED SUWAKA] {ex.Message}");
            }
        }
    }

    public void SetSliderLedBrightness(int sliderIndex, byte brightness)
    {
        if (_hSlider != null && _hSlider.IsValid)
        {
            try
            {
                byte ledIndex = (sliderIndex >= 0 && sliderIndex < 5) ? (byte)sliderIndex : (byte)0xFF;
                int ret = _hSlider.AdjustLedBrightness(1, ledIndex, brightness);
                string label = (sliderIndex >= 0 && sliderIndex < 5) ? $"SUWAKA #{sliderIndex + 1}" : "WSZYSTKICH SUWAKÓW";
                Log($"[JASNOŚĆ {label}] Ustawiono natężenie: {brightness}% (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD JASNOŚCI SUWAKA] {ex.Message}");
            }
        }
    }

    public void SetBaseLedColor(byte r, byte g, byte b)
    {
        if (_hBase != null && _hBase.IsValid)
        {
            try
            {
                _hBase.AdjustLedMode(0, 0, 1, false);
                int ret = _hBase.AdjustLedColor(0, 0, r, g, b, false);
                Log($"[LED PODSTAWKI] Ustawiono sprzętowy kolor RGB({r}, {g}, {b}) (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD LED PODSTAWKI] {ex.Message}");
            }
        }
    }

    public void SetBaseLedBrightness(byte brightness)
    {
        if (_hBase != null && _hBase.IsValid)
        {
            try
            {
                int ret = _hBase.AdjustLedBrightness(0, 0, brightness);
                Log($"[JASNOŚĆ PODSTAWKI] Ustawiono natężenie LED: {brightness}% (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD JASNOŚCI PODSTAWKI] {ex.Message}");
            }
        }
    }

    public void SetRollerLedColor(int rollerIndex, byte r, byte g, byte b)
    {
        if (_hRoller != null && _hRoller.IsValid)
        {
            try
            {
                byte ledIndex = (rollerIndex >= 0 && rollerIndex < 2) ? (byte)rollerIndex : (byte)0xFF;
                _hRoller.AdjustLedMode(2, ledIndex, 1, false);
                int ret = _hRoller.AdjustLedColor(2, ledIndex, r, g, b, false);
                string label = (rollerIndex >= 0 && rollerIndex < 2) ? $"ROLKI #{rollerIndex + 1}" : "WSZYSTKICH ROLEK";
                Log($"[LED {label}] Ustawiono sprzętowy kolor RGB({r}, {g}, {b}) (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD LED ROLKI] {ex.Message}");
            }
        }
    }

    public void SetRollerLedBrightness(int rollerIndex, byte brightness)
    {
        if (_hRoller != null && _hRoller.IsValid)
        {
            try
            {
                byte ledIndex = (rollerIndex >= 0 && rollerIndex < 2) ? (byte)rollerIndex : (byte)0xFF;
                int ret = _hRoller.AdjustLedBrightness(2, ledIndex, brightness);
                string label = (rollerIndex >= 0 && rollerIndex < 2) ? $"ROLKI #{rollerIndex + 1}" : "WSZYSTKICH ROLEK";
                Log($"[JASNOŚĆ {label}] Ustawiono natężenie LED: {brightness}% (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD JASNOŚCI ROLKI] {ex.Message}");
            }
        }
    }

    public void SetKeypadBrightness(byte brightness)
    {
        if (_hKeypad != null && _hKeypad.IsValid)
        {
            try
            {
                int ret = _hKeypad.AdjustBacklight(brightness);
                Log($"[KEYPAD LCD] Jasność podświetlenia ekranu: {brightness}% (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD JASNOŚCI KEYPAD] {ex.Message}");
            }
        }
    }

    public void InitializeKeypadDisplay()
    {
        if (_disposed || _isStandby || _hKeypad == null || !_hKeypad.IsValid) return;

        Task.Run(() =>
        {
            for (byte i = 0; i < 15; i++)
            {
                if (_disposed || _isStandby || _cts.Token.IsCancellationRequested) break;
                UpdateKeyDisplay(i, false);
                Thread.Sleep(30);
            }
        });
    }

    private readonly System.Threading.Lock _keypadLock = new();

    public void UpdateKeyDisplay(int keyIndex, bool isPressed)
    {
        if (_disposed || _isStandby || _hKeypad == null || !_hKeypad.IsValid || keyIndex < 0 || keyIndex >= 15) return;

        lock (_keypadLock)
        {
            try
            {
                byte[] jpeg = CustomKeyImageProvider != null
                    ? CustomKeyImageProvider(keyIndex, isPressed)
                    : (isPressed ? ButtonImageRenderer.GetButtonJpeg(keyIndex + 1, "KLIK", true) : ButtonImageRenderer.GetBlankJpeg());
                _hKeypad.DisplayPictureOnButton((byte)keyIndex, jpeg, jpeg.Length);
            }
            catch (Exception ex)
            {
                Log($"Błąd odświeżania przycisku #{keyIndex + 1}: {ex.Message}");
            }
        }
    }

    public void SetKeyImage(int keyIndex, byte[] jpeg)
    {
        if (_disposed || _isStandby || _hKeypad == null || !_hKeypad.IsValid || keyIndex < 0 || keyIndex >= 15 || jpeg == null || jpeg.Length == 0) return;

        lock (_keypadLock)
        {
            try
            {
                _hKeypad.DisplayPictureOnButton((byte)keyIndex, jpeg, jpeg.Length);
            }
            catch (Exception ex)
            {
                Log($"Błąd wysyłania grafiki na przycisk #{keyIndex + 1}: {ex.Message}");
            }
        }
    }

    private readonly System.Threading.Lock _syncLock = new();

    public void SyncConnectedDevices()
    {
        lock (_syncLock)
        {
            try
            {
                var currentUsb = ScanUsbDevices();
                bool stateChanged = false;

                // 1. Sprawdź, czy któreś z aktualnie połączonych urządzeń zostało fizycznie odłączone
                var disconnected = ConnectedDevices.Where(d => d.IsConnected && !currentUsb.Any(u => u.SerialNumber == d.SerialNumber)).ToList();
                foreach (var dev in disconnected)
                {
                    stateChanged = true;
                    dev.IsConnected = false;
                    Log($"[Hot-Plug] Odłączono moduł: {dev.Name} (SN: {dev.SerialNumber})");

                    switch (dev.ModuleType)
                    {
                        case MasterHubModuleType.KeyDeck15:
                            _hKeypad?.Dispose();
                            _hKeypad = null;
                            break;
                        case MasterHubModuleType.Fader5:
                            _hSlider?.Dispose();
                            _hSlider = null;
                            break;
                        case MasterHubModuleType.RollerDial:
                            _hRoller?.Dispose();
                            _hRoller = null;
                            break;
                        case MasterHubModuleType.BaseStation:
                            _hBase?.Dispose();
                            _hBase = null;
                            break;
                    }
                }

                // 2. Sprawdź, czy podłączono nowe moduły (lub ponownie wpięto w stację dokującą)
                var baseDev = currentUsb.FirstOrDefault(d => d.ModuleType == MasterHubModuleType.BaseStation);
                if (baseDev != null && (_hBase == null || !_hBase.IsValid))
                {
                    ConnectBase(baseDev);
                    stateChanged = true;
                }

                foreach (var dev in currentUsb)
                {
                    if (dev.ModuleType == MasterHubModuleType.BaseStation) continue;

                    bool needConnect = dev.ModuleType switch
                    {
                        MasterHubModuleType.KeyDeck15 => _hKeypad == null || !_hKeypad.IsValid,
                        MasterHubModuleType.Fader5 => _hSlider == null || !_hSlider.IsValid,
                        MasterHubModuleType.RollerDial => _hRoller == null || !_hRoller.IsValid,
                        _ => false
                    };

                    if (needConnect)
                    {
                        stateChanged = true;
                        Log($"[Hot-Plug] Wykryto podłączenie modułu: {dev.Name} (SN: {dev.SerialNumber})");
                        switch (dev.ModuleType)
                        {
                            case MasterHubModuleType.KeyDeck15:
                                ConnectKeypad(dev);
                                break;
                            case MasterHubModuleType.Fader5:
                                ConnectSlider(dev);
                                break;
                            case MasterHubModuleType.RollerDial:
                                ConnectRoller(dev);
                                break;
                        }
                    }
                }

                if (stateChanged)
                {
                    DevicesDiscovered?.Invoke(new List<DeviceInfo>(ConnectedDevices));
                }
            }
            catch (Exception ex)
            {
                Log($"Błąd synchronizacji hot-plug: {ex.Message}");
            }
        }
    }

    private void PollLoop()
    {
        byte[] ioKey = new byte[256];
        byte[] ioSlider = new byte[256];
        byte[] ioRoller = new byte[256];
        byte[] sliderPollBuf = new byte[5];
        int loopCount = 0;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // 1. Sprawdzanie klawiatury 15 klawiszy
                if (_hKeypad != null && _hKeypad.IsValid && !_isStandby)
                {
                    while (true)
                    {
                        int retIo = _hKeypad.ReadInputReport(ioKey, 0);
                        if (retIo == 0)
                        {
                            int keyOffset = -1;
                            if (ioKey[0] == 0x01 && ioKey.Length >= 21) keyOffset = 6;
                            else if (ioKey[0] == 0x00 && ioKey.Length >= 22 && ioKey[1] == 0x01) keyOffset = 7;
                            else if (ioKey[0] == 0x02 && ioKey.Length >= 17) keyOffset = 2;
                            else if (ioKey[0] == 0x00 && ioKey.Length >= 18 && ioKey[1] == 0x02) keyOffset = 3;
                            else if (ioKey[0] == 0x0F && ioKey.Length >= 16) keyOffset = 1;

                            if (keyOffset >= 0)
                            {
                                for (int i = 0; i < 15; i++)
                                {
                                    bool isDown = ioKey[keyOffset + i] == 1;
                                    if (isDown != _keyStates[i])
                                    {
                                        _keyStates[i] = isDown;
                                        KeyStateChanged?.Invoke(i, isDown);
                                        Log($"Klawisz #{i + 1} -> {(isDown ? "WCIŚNIĘTY" : "ZWOLNIONY")}");
                                        if (CustomKeyImageProvider != null)
                                        {
                                            int keyIdx = i;
                                            bool down = isDown;
                                            Task.Run(() => UpdateKeyDisplay(keyIdx, down));
                                        }
                                    }
                                }
                            }
                        }
                        else if (retIo == 7) // 7 = fizycznie odłączony
                        {
                            Log("[Hot-Plug] Moduł 15 klawiszy został fizycznie odłączony.");
                            _hKeypad?.Dispose();
                            _hKeypad = null;
                            var d = ConnectedDevices.FirstOrDefault(x => x.ModuleType == MasterHubModuleType.KeyDeck15);
                            if (d != null) d.IsConnected = false;
                            DevicesDiscovered?.Invoke(new List<DeviceInfo>(ConnectedDevices));
                            Task.Run(SyncConnectedDevices);
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // 2. Płynne sprawdzanie suwaków (Direct Feature Report 0x33, czas wykonania ~0.2ms)
                if (_hSlider != null && _hSlider.IsValid && !_isStandby)
                {
                    int rGet = _hSlider.GetSliderValues(sliderPollBuf);
                    if (rGet == 0)
                    {
                        for (int s = 0; s < 5; s++)
                        {
                            byte val = sliderPollBuf[s];
                            if (val != _lastSliderValues[s])
                            {
                                _lastSliderValues[s] = val;
                                _sliderValues[s] = val;
                                int pct = Math.Clamp((int)val, 0, 100);
                                SliderValueChanged?.Invoke(s, val, pct);
                            }
                        }
                    }
                    else if (rGet == 7)
                    {
                        Log("[Hot-Plug] Moduł 5 suwaków został fizycznie odłączony.");
                        _hSlider?.Dispose();
                        _hSlider = null;
                        var d = ConnectedDevices.FirstOrDefault(x => x.ModuleType == MasterHubModuleType.Fader5);
                        if (d != null) d.IsConnected = false;
                        DevicesDiscovered?.Invoke(new List<DeviceInfo>(ConnectedDevices));
                        Task.Run(SyncConnectedDevices);
                    }
                }

                // 3. Sprawdzanie 2 rolek
                if (_hRoller != null && _hRoller.IsValid && !_isStandby)
                {
                    while (true)
                    {
                        int retRoller = _hRoller.ReadInputReport(ioRoller, 0);
                        if (retRoller == 0)
                        {
                            int rOffset = -1;
                            // CoolTEK M0 Wheel/Knob format: [0x01, type, lenLow, lenHigh, pad0, pad1, Right, Left]
                            if (ioRoller[0] == 0x01 && ioRoller.Length >= 8) rOffset = 6;
                            else if (ioRoller[0] == 0x00 && ioRoller.Length >= 9 && ioRoller[1] == 0x01) rOffset = 7;
                            else if (ioRoller[0] == 0x02 && ioRoller.Length >= 4) rOffset = 2;
                            else if (ioRoller[0] == 0x00 && ioRoller.Length >= 5 && ioRoller[1] == 0x02) rOffset = 3;

                            if (rOffset >= 0 && ioRoller.Length >= rOffset + 2)
                            {
                                byte right = ioRoller[rOffset];
                                byte left = ioRoller[rOffset + 1];

                                if (right != 255)
                                {
                                    // 1 to w dół (-1), 0 (lub inna wartość) to w górę (+1)
                                    int dir = right == 1 ? -1 : +1;
                                    RollerRotated?.Invoke(1, dir);
                                    Log($"Prawa rolka -> {(dir > 0 ? "W GÓRĘ (▲)" : "W DÓŁ (▼)")}");
                                }

                                if (left != 255)
                                {
                                    // 1 to w dół (-1), 0 (lub inna wartość) to w górę (+1)
                                    int dir = left == 1 ? -1 : +1;
                                    RollerRotated?.Invoke(0, dir);
                                    Log($"Lewa rolka -> {(dir > 0 ? "W GÓRĘ (▲)" : "W DÓŁ (▼)")}");
                                }
                            }
                        }
                        else if (retRoller == 7) // 7 = fizycznie odłączony
                        {
                            Log("[Hot-Plug] Moduł 2 rolek został fizycznie odłączony.");
                            _hRoller?.Dispose();
                            _hRoller = null;
                            var d = ConnectedDevices.FirstOrDefault(x => x.ModuleType == MasterHubModuleType.RollerDial);
                            if (d != null) d.IsConnected = false;
                            DevicesDiscovered?.Invoke(new List<DeviceInfo>(ConnectedDevices));
                            Task.Run(SyncConnectedDevices);
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Błąd w pętli odpytywania: {ex.Message}");
            }

            loopCount++;
            Thread.Sleep(10);
        }
    }

    private Task HotPlugBackgroundLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (token.WaitHandle.WaitOne(2000))
                    break;
                SyncConnectedDevices();
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Log($"Błąd w pętli hot-plug: {ex.Message}");
            }
        }
        return Task.CompletedTask;
    }

    private void DisconnectAll()
    {
        _hKeypad?.Dispose();
        _hKeypad = null;

        _hSlider?.Dispose();
        _hSlider = null;

        _hRoller?.Dispose();
        _hRoller = null;

        _hBase?.Dispose();
        _hBase = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch { }

        _hotPlugCts?.Cancel();
        _cts.Cancel();
        _pollThread?.Join(500);

        EnterStandby();
        Thread.Sleep(200);
        DisconnectAll();
        Log("Pomyślnie uśpiono i odłączono wszystkie moduły MasterHub.");
    }
}

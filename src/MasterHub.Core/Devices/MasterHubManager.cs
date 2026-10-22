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
using MasterHub.Core.Profiles;
using MasterHub.Core.Rendering;

namespace MasterHub.Core.Devices;

public class MasterHubManager : IMasterHubManager
{
    private MasterHubDeviceHandle? _hBase;
    private MasterHubDeviceHandle? _hKeypad;
    private MasterHubDeviceHandle? _hSlider;
    private MasterHubDeviceHandle? _hRoller;
    private MasterHubDeviceHandle? _hKnob;
    private MasterHubDeviceHandle? _hDial;

    public bool IsKeypadConnected => _hKeypad != null && _hKeypad.IsValid;
    public bool IsSliderConnected => _hSlider != null && _hSlider.IsValid;
    public bool IsRollerConnected => _hRoller != null && _hRoller.IsValid;
    public bool IsBaseConnected => _hBase != null && _hBase.IsValid;
    public bool IsKnobConnected => _hKnob != null && _hKnob.IsValid;
    public bool IsDialConnected => _hDial != null && _hDial.IsValid;

    private bool _isStandby = false;
    public bool IsStandby => _isStandby;

    private readonly bool[] _keyStates = new bool[15];
    private readonly bool[] _rawPhysicalKeyStates = new bool[15];
    private readonly byte[] _sliderValues = new byte[5];
    private readonly byte[] _lastSliderValues = new byte[5];
    private readonly bool[] _knobPressedStates = new bool[3];
    private bool _dialPressed = false;

    // Przyspieszenie dynamiczne obrotu (Velocity Acceleration)
    private readonly long[] _lastKnobTicks = new long[3];
    private readonly long[] _lastRollerTicks = new long[2];
    private long _lastDialTicks = 0;

    private static int CalculateAccelFactor(ref long lastTicks)
    {
        long now = Stopwatch.GetTimestamp();
        long last = lastTicks;
        lastTicks = now;
        if (last == 0) return 1;

        long elapsedMs = (now - last) * 1000 / Stopwatch.Frequency;
        if (elapsedMs < 50) return 3;
        if (elapsedMs < 110) return 2;
        return 1;
    }

    private static byte FilterFaderValue(byte raw, byte last)
    {
        byte v = raw switch
        {
            <= 2 => 0,
            >= 98 => 100,
            _ => raw
        };

        if (last == v) return last;
        if (Math.Abs((int)last - (int)v) < 2 && v != 0 && v != 100) return last;
        return v;
    }

    private DeckOrientation _hardwareBaseOrientation = DeckOrientation.Horizontal;
    private DeckRotationAngle _effectiveRotationAngle = DeckRotationAngle.Deg0;
    private DeckRotationMode _rotationMode = DeckRotationMode.Auto;
    private int _manualRotationAngle = 0;

    private Thread? _pollThread;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _hotPlugCts;
    private bool _disposed = false;

    // Events
    public event Action<int, bool>? KeyStateChanged; // (keyIndex 0..14, isPressed)
    public event Action<int, byte, int>? SliderValueChanged; // (sliderIndex 0..4, raw 0..255, percent 0..100)
    public event Action<int, int>? RollerRotated; // (rollerIndex 0=Left, 1=Right; direction +1=Up, -1=Down)
    public event Action<int, int>? KnobRotated; // (knobIndex 0..2, delta +1=CW, -1=CCW)
    public event Action<int, bool>? KnobPressed; // (knobIndex 0..2, isPressed)
    public event Action<int>? DialRotated; // (delta +1=CW, -1=CCW)
    public event Action<bool>? DialPressed; // (isPressed)
    public event Action<DeckOrientation>? BaseOrientationChanged;
    public event Action<DeckRotationAngle>? EffectiveRotationChanged;
    public event Action<string>? LogMessage;
    public event Action<List<DeviceInfo>>? DevicesDiscovered;
    public event Action<bool>? StandbyStateChanged;

    public List<DeviceInfo> ConnectedDevices { get; } = new();
    public IReadOnlyList<bool> CurrentKeyStates => _keyStates;
    public IReadOnlyList<byte> CurrentSliderValues => _sliderValues;
    public Func<int, bool, byte[]>? CustomKeyImageProvider { get; set; }

    public DeckOrientation HardwareBaseOrientation => _hardwareBaseOrientation;
    public DeckRotationAngle EffectiveRotationAngle => _effectiveRotationAngle;

    public DeckRotationMode RotationMode
    {
        get => _rotationMode;
        set
        {
            if (_rotationMode != value)
            {
                _rotationMode = value;
                RecalculateEffectiveRotation();
            }
        }
    }

    public int ManualRotationAngle
    {
        get => _manualRotationAngle;
        set
        {
            if (_manualRotationAngle != value)
            {
                _manualRotationAngle = value;
                RecalculateEffectiveRotation();
            }
        }
    }

    public void SetHardwareOrientation(DeckOrientation newOrientation)
    {
        if (_hardwareBaseOrientation != newOrientation)
        {
            _hardwareBaseOrientation = newOrientation;
            Log($"[Czujnik Orientacji] Stacja Dokująca zmieniła pozycję: {newOrientation}");
            BaseOrientationChanged?.Invoke(newOrientation);

            if (_rotationMode == DeckRotationMode.Auto)
            {
                RecalculateEffectiveRotation();
            }
        }
    }

    private void RecalculateEffectiveRotation()
    {
        DeckRotationAngle newAngle = _rotationMode switch
        {
            DeckRotationMode.Manual0 => DeckRotationAngle.Deg0,
            DeckRotationMode.Manual90 => DeckRotationAngle.Deg90,
            DeckRotationMode.Manual180 => DeckRotationAngle.Deg180,
            DeckRotationMode.Manual270 => DeckRotationAngle.Deg270,
            DeckRotationMode.Auto => (_hardwareBaseOrientation == DeckOrientation.Vertical ? DeckRotationAngle.Deg90 : DeckRotationAngle.Deg0),
            _ => DeckRotationAngle.Deg0
        };

        if (_effectiveRotationAngle != newAngle)
        {
            _effectiveRotationAngle = newAngle;
            Log($"[Rotacja Decka] Zastosowano efektywny kąt obrotu: {(int)newAngle}°");
            EffectiveRotationChanged?.Invoke(newAngle);

            if (_hKeypad != null && _hKeypad.IsValid)
            {
                InitializeKeypadDisplay();
            }
        }
    }

    public void Start()
    {
        Log("Inicjalizacja środowiska MasterHub (czysty sterownik Open Source USB HID)...");

        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
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

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            Log("Wykryto zablokowanie sesji Windows -> przełączanie ekranów MasterHub w tryb Standby");
            EnterStandby();
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            Log("Wykryto odblokowanie sesji Windows -> wybudzanie ekranów MasterHub ze stanu Standby");
            WakeUp();
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
                case MasterHubModuleType.Knob3:
                    ConnectKnob(dev);
                    break;
                case MasterHubModuleType.DialDisplay:
                    ConnectDial(dev);
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

            // Odczytaj początkową orientację akcelerometru stacji bazowej
            int rOrient = _hBase.GetBaseOrientation(out var initOrient);
            if (rOrient == 0)
            {
                SetHardwareOrientation(initOrient);
                Log($"Początkowa orientacja stacji dokującej: {initOrient}");
            }
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
            for (int i = 0; i < 5; i++)
            {
                _sliderValues[i] = FilterFaderValue(_sliderValues[i], 0);
                _lastSliderValues[i] = _sliderValues[i];
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

    private void ConnectKnob(DeviceInfo dev)
    {
        Log($"Łączenie z modułem 3 Pokręteł ({dev.SerialNumber})...");
        _hKnob = MasterHubUsbTransport.ConnectDevice(dev.VendorId, dev.ProductId, dev.SerialNumber);
        if (_hKnob != null && _hKnob.IsValid)
        {
            dev.IsConnected = true;
            AddOrUpdateConnectedDevice(dev);
            Log($"Połączono pomyślnie z modułem 3 Pokręteł! (HID: {_hKnob.DevicePath})");

            _hKnob.SetKnobLedMode(0xFF, 1, false);
            _hKnob.SetKnobLedBrightness(0xFF, _currentBrightness);
        }
        else
        {
            dev.IsConnected = false;
            AddOrUpdateConnectedDevice(dev);
            Log("Nie udało się połączyć z modułem 3 Pokręteł.");
        }
    }

    private void ConnectDial(DeviceInfo dev)
    {
        Log($"Łączenie z modułem Pokrętła z Ekranem IPS ({dev.SerialNumber})...");
        _hDial = MasterHubUsbTransport.ConnectDevice(dev.VendorId, dev.ProductId, dev.SerialNumber);
        if (_hDial != null && _hDial.IsValid)
        {
            dev.IsConnected = true;
            AddOrUpdateConnectedDevice(dev);
            Log($"Połączono pomyślnie z modułem Dial IPS! (HID: {_hDial.DevicePath})");

            // KRYTYCZNE: Natychmiastowe włączenie Software Mode, aby zapobiec zawieszeniu MCU!
            int rSw = _hDial.SetDialSwMode(true);
            Log($"Tryb Software Mode dla Dial włączony (wynik: {rSw}).");

            int rBl = _hDial.AdjustDialBacklight(_currentBrightness);
            Log($"Podświetlenie ekranu Dial IPS ustawione na: {_currentBrightness}% (wynik: {rBl}).");
        }
        else
        {
            dev.IsConnected = false;
            AddOrUpdateConnectedDevice(dev);
            Log("Nie udało się połączyć z modułem Dial IPS.");
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
                _hKeypad.FillScreen(0, 0, 0);
                _hKeypad.AdjustBacklight(1); // 1 = bezpieczna minimalna jasność (0 zawiesza MCU na USB)
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

        // 5. Moduł 3 pokręteł (Knobs)
        if (_hKnob != null && _hKnob.IsValid)
        {
            try
            {
                _hKnob.SetKnobLedBrightness(0xFF, 0);
            }
            catch { }
        }

        // 6. Moduł Dial IPS
        if (_hDial != null && _hDial.IsValid)
        {
            try
            {
                _hDial.FillDialColor(0, 0, 0);
                _hDial.AdjustDialBacklight(1); // 1 = bezpieczna minimalna jasność (0 zawiesza MCU na USB)
            }
            catch { }
        }
    }

    public void WakeUp()
    {
        _isStandby = false;
        Log("Wybudzanie modułów MasterHub ze stanu Standby...");

        if (_hKeypad == null && _hSlider == null && _hBase == null && _hRoller == null && _hKnob == null && _hDial == null)
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

        // 5. Moduł 3 pokręteł
        if (_hKnob != null && _hKnob.IsValid)
        {
            try
            {
                _hKnob.SetKnobLedMode(0xFF, 1, false);
                _hKnob.SetKnobLedBrightness(0xFF, _currentBrightness);
            } catch { }
        }

        // 6. Moduł Dial IPS
        if (_hDial != null && _hDial.IsValid)
        {
            try
            {
                _hDial.SetDialSwMode(true);
                _hDial.AdjustDialBacklight(_currentBrightness);
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

        if (_hKnob != null && _hKnob.IsValid)
        {
            try { _hKnob.SetKnobLedBrightness(0xFF, brightness); } catch { }
        }

        if (_hDial != null && _hDial.IsValid)
        {
            try { _hDial.AdjustDialBacklight(brightness); } catch { }
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

    public void SetKnobLedColor(int knobIndex, byte r, byte g, byte b)
    {
        if (_hKnob != null && _hKnob.IsValid)
        {
            try
            {
                byte ledIdx = (knobIndex >= 0 && knobIndex < 3) ? (byte)knobIndex : (byte)0xFF;
                _hKnob.SetKnobLedMode(ledIdx, 1, false);
                int ret = _hKnob.SetKnobLedColor(ledIdx, r, g, b, false);
                string label = (knobIndex >= 0 && knobIndex < 3) ? $"POKRĘTŁA #{knobIndex + 1}" : "WSZYSTKICH POKRĘTEŁ";
                Log($"[LED {label}] Ustawiono sprzętowy kolor RGB({r}, {g}, {b}) (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD LED POKRĘTŁA] {ex.Message}");
            }
        }
    }

    public void SetKnobLedBrightness(int knobIndex, byte brightness)
    {
        if (_hKnob != null && _hKnob.IsValid)
        {
            try
            {
                byte ledIdx = (knobIndex >= 0 && knobIndex < 3) ? (byte)knobIndex : (byte)0xFF;
                int ret = _hKnob.SetKnobLedBrightness(ledIdx, brightness);
                string label = (knobIndex >= 0 && knobIndex < 3) ? $"POKRĘTŁA #{knobIndex + 1}" : "WSZYSTKICH POKRĘTEŁ";
                Log($"[JASNOŚĆ {label}] Ustawiono natężenie LED: {brightness}% (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD JASNOŚCI POKRĘTŁA] {ex.Message}");
            }
        }
    }

    public void SetKnobLedMode(int knobIndex, KnobLedMode mode)
    {
        if (_hKnob != null && _hKnob.IsValid)
        {
            try
            {
                byte ledIdx = (knobIndex >= 0 && knobIndex < 3) ? (byte)knobIndex : (byte)0xFF;
                int ret = _hKnob.SetKnobLedMode(ledIdx, (byte)mode, false);
                string label = (knobIndex >= 0 && knobIndex < 3) ? $"POKRĘTŁA #{knobIndex + 1}" : "WSZYSTKICH POKRĘTEŁ";
                Log($"[TRYB LED {label}] Ustawiono tryb: {mode} (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD TRYBU LED POKRĘTŁA] {ex.Message}");
            }
        }
    }

    public void SetDialBrightness(byte brightness)
    {
        if (_hDial != null && _hDial.IsValid)
        {
            try
            {
                int ret = _hDial.AdjustDialBacklight(brightness);
                Log($"[JASNOŚĆ DIAL IPS] Ustawiono podświetlenie ekranu: {brightness}% (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD JASNOŚCI DIAL IPS] {ex.Message}");
            }
        }
    }

    public void SetDialColor(byte r, byte g, byte b)
    {
        if (_hDial != null && _hDial.IsValid)
        {
            try
            {
                int ret = _hDial.FillDialColor(r, g, b);
                Log($"[KOLOR DIAL IPS] Ustawiono jednolity kolor RGB({r}, {g}, {b}) (kod: {ret})");
            }
            catch (Exception ex)
            {
                Log($"[BŁĄD KOLORU DIAL IPS] {ex.Message}");
            }
        }
    }

    private readonly System.Threading.Lock _dialLock = new();

    public void SetDialImage(byte[] jpeg)
    {
        if (_disposed || _isStandby || _hDial == null || !_hDial.IsValid || jpeg == null || jpeg.Length == 0) return;

        lock (_dialLock)
        {
            try
            {
                byte[] finalJpeg = ButtonImageRenderer.RenderDialImage(jpeg, "Cover");
                _hDial.DisplayPictureOnDial(finalJpeg, finalJpeg.Length);
            }
            catch (Exception ex)
            {
                Log($"Błąd przesyłania obrazu 240x240 na ekran Dial IPS: {ex.Message}");
            }
        }
    }

    public Task<bool> SetDialImageAsync(byte[] imageBytes)
    {
        return Task.Run(() =>
        {
            try
            {
                SetDialImage(imageBytes);
                return true;
            }
            catch
            {
                return false;
            }
        });
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

                int angle = (int)_effectiveRotationAngle;
                int physicalKey = KeyOrientationHelper.LogicalToPhysicalIndex(keyIndex, angle);
                byte[] finalJpeg = (angle != 0) ? ButtonImageRenderer.RotateJpeg(jpeg, angle) : jpeg;

                _hKeypad.DisplayPictureOnButton((byte)physicalKey, finalJpeg, finalJpeg.Length);
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
                int angle = (int)_effectiveRotationAngle;
                int physicalKey = KeyOrientationHelper.LogicalToPhysicalIndex(keyIndex, angle);
                byte[] finalJpeg = (angle != 0) ? ButtonImageRenderer.RotateJpeg(jpeg, angle) : jpeg;

                _hKeypad.DisplayPictureOnButton((byte)physicalKey, finalJpeg, finalJpeg.Length);
            }
            catch (Exception ex)
            {
                Log($"Błąd wysyłania grafiki na przycisk #{keyIndex + 1}: {ex.Message}");
            }
        }
    }

    public void SetPanoramicWallpaper(byte[] jpeg)
    {
        if (_disposed || _isStandby || _hKeypad == null || !_hKeypad.IsValid || jpeg == null || jpeg.Length == 0) return;

        lock (_keypadLock)
        {
            try
            {
                int angle = (int)_effectiveRotationAngle;
                byte[] finalJpeg = (angle == 180) ? ButtonImageRenderer.RotatePanoramicJpeg(jpeg, 180) : jpeg;

                _hKeypad.DisplayPanoramicPicture(finalJpeg, finalJpeg.Length);
            }
            catch (Exception ex)
            {
                Log($"Błąd wysyłania pełnopanelowego obrazu 480x272: {ex.Message}");
            }
        }
    }

    public Task<bool> SetPanoramicWallpaperAsync(byte[] imageBytes)
    {
        return Task.Run(() =>
        {
            try
            {
                SetPanoramicWallpaper(imageBytes);
                return true;
            }
            catch
            {
                return false;
            }
        });
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
                        case MasterHubModuleType.Knob3:
                            _hKnob?.Dispose();
                            _hKnob = null;
                            break;
                        case MasterHubModuleType.DialDisplay:
                            try { _hDial?.SetDialSwMode(false); } catch { }
                            _hDial?.Dispose();
                            _hDial = null;
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
                        MasterHubModuleType.Knob3 => _hKnob == null || !_hKnob.IsValid,
                        MasterHubModuleType.DialDisplay => _hDial == null || !_hDial.IsValid,
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
                            case MasterHubModuleType.Knob3:
                                ConnectKnob(dev);
                                break;
                            case MasterHubModuleType.DialDisplay:
                                ConnectDial(dev);
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
        byte[] ioBase = new byte[256];
        byte[] ioKnob = new byte[256];
        byte[] ioDial = new byte[256];
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
                                int angle = (int)_effectiveRotationAngle;
                                for (int i = 0; i < 15; i++)
                                {
                                    bool isDown = ioKey[keyOffset + i] == 1;
                                    if (isDown != _rawPhysicalKeyStates[i])
                                    {
                                        _rawPhysicalKeyStates[i] = isDown;
                                        int logicalKey = KeyOrientationHelper.PhysicalToLogicalIndex(i, angle);
                                        if (logicalKey >= 0 && logicalKey < 15)
                                        {
                                            if (isDown != _keyStates[logicalKey])
                                            {
                                                _keyStates[logicalKey] = isDown;
                                                KeyStateChanged?.Invoke(logicalKey, isDown);
                                                Log($"Klawisz #{logicalKey + 1} (fizyczny #{i + 1}) -> {(isDown ? "WCIŚNIĘTY" : "ZWOLNIONY")}");
                                                if (CustomKeyImageProvider != null)
                                                {
                                                    int keyIdx = logicalKey;
                                                    bool down = isDown;
                                                    Task.Run(() => UpdateKeyDisplay(keyIdx, down));
                                                }
                                            }
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

                // 2. Obsługa suwaków: natychmiastowe zdarzenia interrupt (120 Hz) + okresowy fallback Feature 0x33
                if (_hSlider != null && _hSlider.IsValid && !_isStandby)
                {
                    bool hadInput = false;
                    while (true)
                    {
                        int retSlider = _hSlider.ReadInputReport(ioSlider, 0);
                        if (retSlider == 0)
                        {
                            hadInput = true;
                            ParseSliderReport(ioSlider);
                        }
                        else if (retSlider == 7) // 7 = fizycznie odłączony
                        {
                            Log("[Hot-Plug] Moduł 5 suwaków został fizycznie odłączony.");
                            _hSlider?.Dispose();
                            _hSlider = null;
                            var d = ConnectedDevices.FirstOrDefault(x => x.ModuleType == MasterHubModuleType.Fader5);
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

                    // Fallback polling co 10 obiegów (~100ms), jeśli moduł nie przysłał raportu na przerwaniu
                    if (!hadInput && _hSlider != null && loopCount % 10 == 0)
                    {
                        int rGet = _hSlider.GetSliderValues(sliderPollBuf);
                        if (rGet == 0)
                        {
                            for (int s = 0; s < 5; s++)
                            {
                                byte val = FilterFaderValue(sliderPollBuf[s], _lastSliderValues[s]);
                                if (val != _lastSliderValues[s])
                                {
                                    _lastSliderValues[s] = val;
                                    _sliderValues[s] = val;
                                    int pct = Math.Clamp((int)val, 0, 100);
                                    SliderValueChanged?.Invoke(s, val, pct);
                                }
                            }
                        }
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
                                // rOffset to Prawa rolka (Right), a rOffset + 1 to Lewa rolka (Left)
                                byte right = ioRoller[rOffset];
                                byte left = ioRoller[rOffset + 1];

                                if (left != 255)
                                {
                                    int dir = left == 0 ? +1 : -1;
                                    int factor = CalculateAccelFactor(ref _lastRollerTicks[0]);
                                    int delta = dir * factor;
                                    RollerRotated?.Invoke(0, delta);
                                    Log($"Lewa rolka (Roller #1) -> {(dir > 0 ? "W GÓRĘ (▲)" : "W DÓŁ (▼)")} [x{factor}]");
                                }

                                if (right != 255)
                                {
                                    int dir = right == 0 ? +1 : -1;
                                    int factor = CalculateAccelFactor(ref _lastRollerTicks[1]);
                                    int delta = dir * factor;
                                    RollerRotated?.Invoke(1, delta);
                                    Log($"Prawa rolka (Roller #2) -> {(dir > 0 ? "W GÓRĘ (▲)" : "W DÓŁ (▼)")} [x{factor}]");
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

                // 4. Sprawdzanie Stacji Bazowej (akcelerometr / orientacja sprzętowa)
                var hBase = _hBase;
                if (hBase != null && hBase.IsValid && !_isStandby)
                {
                    while (true)
                    {
                        int retBase = hBase.ReadInputReport(ioBase, 0);
                        if (retBase == 0)
                        {
                            int val = -1;
                            for (int b = 0; b < Math.Min(ioBase.Length - 3, 8); b++)
                            {
                                if (ioBase[b] == 0x2B)
                                {
                                    // Format raportu 0x2B: [0x2B, length, event_id, event_data, ...]
                                    // event_id 0x00 to Heartbeat (ignorujemy)
                                    // event_id 0x01 to Zmiana orientacji: 0x01 = Pozioma (Horizontal, płasko na biurku), 0x00 = Pionowa (Vertical, postawiona pionowo)
                                    byte eventId = ioBase[b + 2];
                                    if (eventId == 0x01 && b + 3 < ioBase.Length)
                                    {
                                        byte orientByte = ioBase[b + 3];
                                        if (orientByte == 0x00 || orientByte == 0x01)
                                        {
                                            val = orientByte;
                                        }
                                    }
                                    break;
                                }
                            }

                            if (val >= 0)
                            {
                                var detectedOrient = (val == 1) ? DeckOrientation.Horizontal : DeckOrientation.Vertical;
                                SetHardwareOrientation(detectedOrient);
                            }
                        }
                        else if (retBase == 7) // 7 = fizycznie odłączony
                        {
                            Log("[Hot-Plug] Stacja Bazowa została fizycznie odłączona.");
                            _hBase?.Dispose();
                            _hBase = null;
                            var d = ConnectedDevices.FirstOrDefault(x => x.ModuleType == MasterHubModuleType.BaseStation);
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

                    // Okresowa weryfikacja przez Feature Report 0x35 co ~120 cykli (~1.8 s)
                    if (loopCount % 120 == 0)
                    {
                        int rQuery = hBase.GetBaseOrientation(out var polledOrient);
                        if (rQuery == 0)
                        {
                            SetHardwareOrientation(polledOrient);
                        }
                    }
                }

                // 5. Sprawdzanie modułu 3 Pokręteł (Tag 0x2A)
                if (_hKnob != null && _hKnob.IsValid && !_isStandby)
                {
                    while (true)
                    {
                        int retKnob = _hKnob.ReadInputReport(ioKnob, 0);
                        if (retKnob == 0)
                        {
                            ParseKnobReport(ioKnob);
                        }
                        else if (retKnob == 7) // 7 = fizycznie odłączony
                        {
                            Log("[Hot-Plug] Moduł 3 pokręteł został fizycznie odłączony.");
                            _hKnob?.Dispose();
                            _hKnob = null;
                            var d = ConnectedDevices.FirstOrDefault(x => x.ModuleType == MasterHubModuleType.Knob3);
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

                // 6. Sprawdzanie modułu Dial IPS (Tag 0x44)
                if (_hDial != null && _hDial.IsValid && !_isStandby)
                {
                    while (true)
                    {
                        int retDial = _hDial.ReadInputReport(ioDial, 0);
                        if (retDial == 0)
                        {
                            ParseDialReport(ioDial);
                        }
                        else if (retDial == 7) // 7 = fizycznie odłączony
                        {
                            Log("[Hot-Plug] Moduł Dial IPS został fizycznie odłączony.");
                            try { _hDial?.SetDialSwMode(false); } catch { }
                            _hDial?.Dispose();
                            _hDial = null;
                            var d = ConnectedDevices.FirstOrDefault(x => x.ModuleType == MasterHubModuleType.DialDisplay);
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

    public void ParseSliderReport(byte[] report)
    {
        if (report == null || report.Length < 11) return;

        int tagIdx = -1;
        for (int i = 0; i < Math.Min(report.Length - 10, 16); i++)
        {
            if (report[i] == 0x2A && i > 0 && (report[i - 1] == 0x01 || (i > 1 && report[i - 2] == 0x01)))
            {
                tagIdx = i;
                break;
            }
        }
        if (tagIdx < 0) return;
        if (report.Length < tagIdx + 10) return;

        byte eventId = report[tagIdx + 3]; // 0x02 (Ruch suwakiem)
        byte count = report[tagIdx + 4];   // 0x05 (5 suwaków)
        if (eventId == 0x02 && count == 0x05)
        {
            for (int s = 0; s < 5; s++)
            {
                byte raw = report[tagIdx + 5 + s];
                byte val = FilterFaderValue(raw, _lastSliderValues[s]);
                if (val != _lastSliderValues[s])
                {
                    _lastSliderValues[s] = val;
                    _sliderValues[s] = val;
                    int pct = Math.Clamp((int)val, 0, 100);
                    SliderValueChanged?.Invoke(s, val, pct);
                }
            }
        }
    }

    public void ParseKnobReport(byte[] report)
    {
        if (report == null || report.Length < 7) return;

        int tagIdx = -1;
        for (int i = 0; i < Math.Min(report.Length - 7, 16); i++)
        {
            if (report[i] == 0x2A && i > 0 && (report[i - 1] == 0x01 || (i > 1 && report[i - 2] == 0x01)))
            {
                tagIdx = i;
                break;
            }
        }
        if (tagIdx < 0) return;

        if (report.Length < tagIdx + 8) return;

        byte eventId = report[tagIdx + 3];
        byte k0 = report[tagIdx + 5];
        byte k1 = report[tagIdx + 6];
        byte k2 = report[tagIdx + 7];

        if (eventId == 0x00) // Rotation
        {
            // 0x01 = CW (+1), 0x00 = CCW (-1), 0xFF = no change
            byte[] vals = [k0, k1, k2];
            for (int k = 0; k < 3; k++)
            {
                if (vals[k] == 0x01)
                {
                    int factor = CalculateAccelFactor(ref _lastKnobTicks[k]);
                    int delta = +1 * factor;
                    KnobRotated?.Invoke(k, delta);
                    Log($"Pokrętło #{k + 1} -> OBRÓT W PRAWO (CW ▲) [x{factor}]");
                }
                else if (vals[k] == 0x00)
                {
                    int factor = CalculateAccelFactor(ref _lastKnobTicks[k]);
                    int delta = -1 * factor;
                    KnobRotated?.Invoke(k, delta);
                    Log($"Pokrętło #{k + 1} -> OBRÓT W LEWO (CCW ▼) [x{factor}]");
                }
            }
        }
        else if (eventId == 0x03) // Click
        {
            // 0x01 = pressed, 0x00 = released
            byte[] states = [k0, k1, k2];
            for (int k = 0; k < 3; k++)
            {
                bool isDown = states[k] == 0x01;
                if (isDown != _knobPressedStates[k])
                {
                    _knobPressedStates[k] = isDown;
                    KnobPressed?.Invoke(k, isDown);
                    Log($"Pokrętło #{k + 1} -> {(isDown ? "WCIŚNIĘTE" : "ZWOLNIONE")}");
                }
            }
        }
    }

    public void ParseDialReport(byte[] report)
    {
        if (report == null || report.Length < 6) return;

        int tagIdx = -1;
        for (int i = 0; i < Math.Min(report.Length - 5, 16); i++)
        {
            if (report[i] == 0x44 && i > 0 && (report[i - 1] == 0x01 || (i > 1 && report[i - 2] == 0x01)))
            {
                tagIdx = i;
                break;
            }
        }
        if (tagIdx < 0) return;

        if (report.Length < tagIdx + 6) return;

        byte eventId = report[tagIdx + 3];
        byte data = report[tagIdx + 5];

        if (eventId == 0x03) // Rotation
        {
            // dir: 0 = CW (+1), 1 = CCW (-1)
            int dir = (data == 0) ? +1 : -1;
            int factor = CalculateAccelFactor(ref _lastDialTicks);
            int delta = dir * factor;
            DialRotated?.Invoke(delta);
            Log($"Dial IPS -> {(dir > 0 ? "OBRÓT W PRAWO (CW ▲)" : "OBRÓT W LEWO (CCW ▼)")} [x{factor}]");
        }
        else if (eventId == 0x04) // Click
        {
            // state: 1 = pressed, 0 = released
            bool isDown = data == 1;
            if (isDown != _dialPressed)
            {
                _dialPressed = isDown;
                DialPressed?.Invoke(isDown);
                Log($"Dial IPS -> Przycisk {(isDown ? "WCIŚNIĘTY" : "ZWOLNIONY")}");
            }
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
        if (_hKeypad != null)
        {
            try { _hKeypad.SetSwMode(false); } catch { }
            _hKeypad.Dispose();
            _hKeypad = null;
        }

        _hSlider?.Dispose();
        _hSlider = null;

        _hRoller?.Dispose();
        _hRoller = null;

        _hBase?.Dispose();
        _hBase = null;

        _hKnob?.Dispose();
        _hKnob = null;

        if (_hDial != null)
        {
            try { _hDial.SetDialSwMode(false); } catch { }
            _hDial.Dispose();
            _hDial = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
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

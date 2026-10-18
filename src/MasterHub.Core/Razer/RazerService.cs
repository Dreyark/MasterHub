using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MasterHub.Core.Razer;

public class RazerService : IRazerService, IDisposable
{
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;

    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_DEVICEINTERFACE = 0x00000010;

    private static readonly Guid GUID_DEVINTERFACE_HID = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, ref int RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    private readonly Lock _lock = new();
    private SafeFileHandle? _deviceHandle;
    private string? _cachedDevicePath;
    private int _currentDpi = 800;
    private int _currentStageIndex = 1;
    private int _currentProfileIndex = 1;
    private int _currentPollingRate = 1000;
    private int _batteryPercentage = 100;
    private bool _isCharging = false;
    private bool _isDeviceConnected;
    private string _connectedDeviceName = "Razer Mouse";
    private Timer? _batteryTimer;

    public bool IsDeviceConnected => _isDeviceConnected;
    public string ConnectedDeviceName => _connectedDeviceName;
    public string DevicePath => _cachedDevicePath ?? "";
    public int CurrentDpi => _currentDpi;
    public int CurrentStageIndex => _currentStageIndex;
    public int CurrentProfileIndex => _currentProfileIndex;
    public int CurrentPollingRate => _currentPollingRate;
    public int BatteryPercentage => _batteryPercentage;
    public bool IsCharging => _isCharging;

    public string BatteryStatusText => _isCharging 
        ? $"⚡ {_batteryPercentage}% (Ładowanie)" 
        : $"🔋 {_batteryPercentage}%";

    public IReadOnlyList<int> DpiStages { get; } = new List<int> { 400, 800, 1600, 3200, 6400 };
    public IReadOnlyList<int> SupportedPollingRates { get; } = new List<int> { 125, 500, 1000, 2000, 4000, 8000 };

    public event Action<int, int>? DpiChanged;
    public event Action<int>? ProfileChanged;
    public event Action<int>? PollingRateChanged;
    public event Action<int, bool>? BatteryChanged;
    public event Action<string>? LogMessage;

    public RazerService()
    {
        RefreshDevice();
        _batteryTimer = new Timer(_ => QueryBattery(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    public bool RefreshDevice()
    {
        lock (_lock)
        {
            CloseHandle();
            var (path, pid) = FindRazerControlPath();
            if (path != null)
            {
                _cachedDevicePath = path;
                _isDeviceConnected = true;
                _connectedDeviceName = ResolveRazerModelName(pid);

                LogMessage?.Invoke($"[RAZER] Wykryto mysz: {_connectedDeviceName}");

                // Spróbuj odpytać o aktualne DPI
                int hardwareDpi = QueryCurrentHardwareDpi();
                if (hardwareDpi > 0)
                {
                    _currentDpi = hardwareDpi;
                    for (int i = 0; i < DpiStages.Count; i++)
                    {
                        if (DpiStages[i] == hardwareDpi)
                        {
                            _currentStageIndex = i;
                            break;
                        }
                    }
                }

                // Odpytaj o Polling Rate
                QueryCurrentHardwarePollingRate();

                // Odpytaj o Baterię
                QueryBattery();

                return true;
            }
            else
            {
                _isDeviceConnected = false;
                _cachedDevicePath = null;
                return false;
            }
        }
    }

    public bool SetDpi(int dpi)
    {
        if (dpi < 100) dpi = 100;
        if (dpi > 35000) dpi = 35000;

        lock (_lock)
        {
            EnsureDeviceConnected();

            byte[] report = CreateRazerReport(0x04, 0x05, 7, [
                0x00, // Variable DPI
                (byte)(dpi >> 8),
                (byte)(dpi & 0xFF),
                (byte)(dpi >> 8),
                (byte)(dpi & 0xFF),
                0x00,
                0x00
            ]);

            // Bezpośrednia, błyskawiczna wysyłka przez USB Control Transfer (<0.5 ms)
            bool success = SendFeatureReport(report);

            if (success)
            {
                _currentDpi = dpi;

                // Znajdź najbliższy etap zdefiniowany
                for (int i = 0; i < DpiStages.Count; i++)
                {
                    if (DpiStages[i] == dpi)
                    {
                        _currentStageIndex = i;
                        break;
                    }
                }

                DpiChanged?.Invoke(_currentDpi, _currentStageIndex);
                LogMessage?.Invoke($"[RAZER] 🎯 Ustawiono sprzętowe DPI: {dpi} (Etap #{_currentStageIndex + 1}/{DpiStages.Count})");
                return true;
            }

            LogMessage?.Invoke($"[RAZER] Błąd ustawiania DPI {dpi}");
            return false;
        }
    }

    public int CycleDpi(int direction)
    {
        lock (_lock)
        {
            int newStage = _currentStageIndex + (direction > 0 ? 1 : -1);
            if (newStage >= DpiStages.Count) newStage = DpiStages.Count - 1;
            if (newStage < 0) newStage = 0;

            _currentStageIndex = newStage;
            int targetDpi = DpiStages[newStage];
            SetDpi(targetDpi);
            return targetDpi;
        }
    }

    public int CycleDpiNext()
    {
        lock (_lock)
        {
            int newStage = (_currentStageIndex + 1) % DpiStages.Count;
            _currentStageIndex = newStage;
            int targetDpi = DpiStages[newStage];
            SetDpi(targetDpi);
            return targetDpi;
        }
    }

    public bool SwitchProfile(int profileIndex)
    {
        if (profileIndex < 1) profileIndex = 1;
        if (profileIndex > 5) profileIndex = 5;

        lock (_lock)
        {
            _currentProfileIndex = profileIndex;
            ProfileChanged?.Invoke(profileIndex);
            LogMessage?.Invoke($"[RAZER] 🐍 Przełączono profil aplikacji na: #{profileIndex}");
            return true;
        }
    }

    public int CycleProfile(int direction)
    {
        lock (_lock)
        {
            int newProfile = _currentProfileIndex + (direction > 0 ? 1 : -1);
            if (newProfile > 5) newProfile = 1;
            if (newProfile < 1) newProfile = 5;

            SwitchProfile(newProfile);
            return newProfile;
        }
    }

    public bool SetPollingRate(int hz)
    {
        lock (_lock)
        {
            EnsureDeviceConnected();

            bool anySuccess = false;

            // 1. Nowoczesny protokół 8K / HyperPolling (Razer Viper Mini SE, Viper V2/V3 Pro, DeathAdder V3 Pro itd.)
            // command_class = 0x00, command_id = 0x40, data_size = 0x02
            byte modernByte = HzToModernByte(hz);
            if (modernByte != 0)
            {
                // Live apply (args[0] = 0x00)
                byte[] liveReport = CreateRazerReport(0x00, 0x40, 2, [0x00, modernByte]);
                bool liveOk = SendFeatureReport(liveReport);

                // Onboard memory store (args[0] = 0x01)
                byte[] storeReport = CreateRazerReport(0x00, 0x40, 2, [0x01, modernByte]);
                bool storeOk = SendFeatureReport(storeReport);

                if (liveOk || storeOk)
                {
                    anySuccess = true;
                }
            }

            // 2. Starszy protokół legacy (0x00, 0x05, data_size = 0x01)
            byte legacyByte = HzToLegacyByte(hz);
            if (legacyByte != 0)
            {
                byte[] legacyReport = CreateRazerReport(0x00, 0x05, 1, [legacyByte]);
                if (SendFeatureReport(legacyReport))
                {
                    anySuccess = true;
                }
            }

            if (anySuccess)
            {
                _currentPollingRate = hz;
                PollingRateChanged?.Invoke(hz);
                LogMessage?.Invoke($"[RAZER] ⚡ Ustawiono częstotliwość próbkowania: {hz} Hz");
                return true;
            }

            LogMessage?.Invoke($"[RAZER] Błąd ustawiania częstotliwości próbkowania: {hz} Hz");
            return false;
        }
    }

    public int CyclePollingRate(int direction)
    {
        lock (_lock)
        {
            int idx = -1;
            for (int i = 0; i < SupportedPollingRates.Count; i++)
            {
                if (SupportedPollingRates[i] == _currentPollingRate)
                {
                    idx = i;
                    break;
                }
            }
            if (idx == -1) idx = 2; // 1000 Hz domyślnie

            int nextIdx = idx + (direction > 0 ? 1 : -1);
            if (nextIdx >= SupportedPollingRates.Count) nextIdx = SupportedPollingRates.Count - 1;
            if (nextIdx < 0) nextIdx = 0;

            int nextHz = SupportedPollingRates[nextIdx];
            SetPollingRate(nextHz);
            return nextHz;
        }
    }

    public int CyclePollingRateNext()
    {
        lock (_lock)
        {
            int idx = -1;
            for (int i = 0; i < SupportedPollingRates.Count; i++)
            {
                if (SupportedPollingRates[i] == _currentPollingRate)
                {
                    idx = i;
                    break;
                }
            }
            if (idx == -1) idx = 2;

            int nextIdx = (idx + 1) % SupportedPollingRates.Count;
            int nextHz = SupportedPollingRates[nextIdx];
            SetPollingRate(nextHz);
            return nextHz;
        }
    }

    public void SendShortcut(RazerShortcutAction action)
    {
        switch (action)
        {
            case RazerShortcutAction.NextProfile:
                CycleProfile(1);
                break;
            case RazerShortcutAction.PrevProfile:
                CycleProfile(-1);
                break;
            case RazerShortcutAction.Profile1:
                SwitchProfile(1);
                break;
            case RazerShortcutAction.Profile2:
                SwitchProfile(2);
                break;
            case RazerShortcutAction.Profile3:
                SwitchProfile(3);
                break;
            case RazerShortcutAction.Profile4:
                SwitchProfile(4);
                break;
            case RazerShortcutAction.Profile5:
                SwitchProfile(5);
                break;
            case RazerShortcutAction.DpiUp:
                CycleDpi(1);
                break;
            case RazerShortcutAction.DpiDown:
                CycleDpi(-1);
                break;
            case RazerShortcutAction.DpiCycle:
                CycleDpiNext();
                break;
        }
    }

    private void EnsureDeviceConnected()
    {
        if (string.IsNullOrWhiteSpace(_cachedDevicePath))
        {
            RefreshDevice();
        }
    }

    private SafeFileHandle? GetDeviceHandle()
    {
        if (_deviceHandle != null && !_deviceHandle.IsInvalid && !_deviceHandle.IsClosed)
        {
            return _deviceHandle;
        }

        if (string.IsNullOrWhiteSpace(_cachedDevicePath))
        {
            var (path, _) = FindRazerControlPath();
            _cachedDevicePath = path;
            if (string.IsNullOrWhiteSpace(_cachedDevicePath)) return null;
        }

        try
        {
            CloseHandle();
            _deviceHandle = CreateFile(
                _cachedDevicePath,
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (_deviceHandle.IsInvalid)
            {
                _deviceHandle.Dispose();
                _deviceHandle = null;
                return null;
            }

            return _deviceHandle;
        }
        catch
        {
            _deviceHandle = null;
            return null;
        }
    }

    private void CloseHandle()
    {
        try
        {
            if (_deviceHandle != null && !_deviceHandle.IsClosed)
            {
                _deviceHandle.Dispose();
            }
        }
        catch { }
        finally
        {
            _deviceHandle = null;
        }
    }

    private bool SendFeatureReport(byte[] report)
    {
        lock (_lock)
        {
            SafeFileHandle? handle = GetDeviceHandle();
            if (handle == null || handle.IsInvalid) return false;

            try
            {
                bool success = HidD_SetFeature(handle, report, report.Length);
                if (!success)
                {
                    CloseHandle();
                    handle = GetDeviceHandle();
                    if (handle != null && !handle.IsInvalid)
                    {
                        success = HidD_SetFeature(handle, report, report.Length);
                    }
                }
                return success;
            }
            catch
            {
                CloseHandle();
                return false;
            }
        }
    }

    private bool SendAndReceiveReport(byte[] report, out byte[] resp)
    {
        resp = new byte[91];
        lock (_lock)
        {
            SafeFileHandle? handle = GetDeviceHandle();
            if (handle == null || handle.IsInvalid) return false;

            try
            {
                bool setOk = HidD_SetFeature(handle, report, report.Length);
                if (!setOk)
                {
                    CloseHandle();
                    handle = GetDeviceHandle();
                    if (handle == null || !HidD_SetFeature(handle, report, report.Length))
                    {
                        return false;
                    }
                }

                // Sprawdzenie statusu odpowiedzi (max 5 prób po 3ms = 15 ms, bez zbędnego zamrażania)
                for (int i = 0; i < 5; i++)
                {
                    Thread.Sleep(3);
                    if (HidD_GetFeature(handle, resp, resp.Length))
                    {
                        if (resp[1] != 0x01) // 0x01 = BUSY, 0x02 = SUCCESS
                        {
                            return resp[1] == 0x02;
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }

    private int QueryCurrentHardwareDpi()
    {
        try
        {
            byte[] req = CreateRazerReport(0x04, 0x85, 7, [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
            if (SendAndReceiveReport(req, out byte[] resp) || resp[1] == 0x02)
            {
                int x = (resp[10] << 8) | resp[11];
                if (x >= 100 && x <= 35000) return x;
            }
        }
        catch { }

        return 0;
    }

    private void QueryCurrentHardwarePollingRate()
    {
        try
        {
            // 1. Nowoczesne zapytanie 0x00, 0xC0 (GET_POLLING_RATE dla myszy 8K / HyperPolling)
            byte[] reqModern = CreateRazerReport(0x00, 0xC0, 1, [0x00]);
            if (SendAndReceiveReport(reqModern, out byte[] respModern) && respModern[1] == 0x02)
            {
                // W protokole 8K bajt częstotliwości znajduje się w respModern[10] (args[1])
                int hz = ModernByteToHz(respModern[10]);
                if (hz > 0)
                {
                    _currentPollingRate = hz;
                    PollingRateChanged?.Invoke(hz);
                    return;
                }
            }

            // 2. Starsze zapytanie 0x00, 0x85 (GET_POLLING_RATE dla starszych myszy)
            byte[] reqLegacy = CreateRazerReport(0x00, 0x85, 1, [0x00]);
            if (SendAndReceiveReport(reqLegacy, out byte[] respLegacy) && respLegacy[1] == 0x02)
            {
                int hz = LegacyByteToHz(respLegacy[9]);
                if (hz > 0)
                {
                    _currentPollingRate = hz;
                    PollingRateChanged?.Invoke(hz);
                }
            }
        }
        catch { }
    }

    private void QueryBattery()
    {
        if (!_isDeviceConnected) return;

        try
        {
            byte[] req = CreateRazerReport(0x07, 0x80, 2, [0x00, 0x00]);
            if (SendAndReceiveReport(req, out byte[] resp) && (resp[7] == 0x07 && resp[8] == 0x80))
            {
                bool isCharging = resp[9] == 0x01;
                int rawLevel = resp[10];
                int pct = Math.Clamp((int)Math.Round(rawLevel / 255.0 * 100.0), 0, 100);
                if (rawLevel > 0 && rawLevel <= 100 && resp[11] == 0)
                {
                    pct = rawLevel;
                }

                if (pct > 0)
                {
                    _batteryPercentage = pct;
                    _isCharging = isCharging;
                    BatteryChanged?.Invoke(_batteryPercentage, _isCharging);
                }
            }
        }
        catch { }
    }

    private static byte HzToModernByte(int hz) => hz switch
    {
        8000 => 0x01,
        4000 => 0x02,
        2000 => 0x04,
        1000 => 0x08,
        500  => 0x10,
        250  => 0x20,
        125  => 0x40,
        _    => 0x08
    };

    private static int ModernByteToHz(byte b) => b switch
    {
        0x01 => 8000,
        0x02 => 4000,
        0x04 => 2000,
        0x08 => 1000,
        0x10 => 500,
        0x20 => 250,
        0x40 => 125,
        _    => 1000
    };

    private static byte HzToLegacyByte(int hz) => hz switch
    {
        1000 => 0x01,
        500  => 0x02,
        125  => 0x08,
        _    => 0x01
    };

    private static int LegacyByteToHz(byte b) => b switch
    {
        0x01 => 1000,
        0x02 => 500,
        0x08 => 125,
        _    => 1000
    };

    private static (string? Path, ushort Pid) FindRazerControlPath()
    {
        Guid hidGuid = GUID_DEVINTERFACE_HID;
        IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1)) return (null, 0);

        int index = 0;
        SP_DEVICE_INTERFACE_DATA interfaceData = new() { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
        string? candidateFallback = null;
        ushort fallbackPid = 0;

        try
        {
            while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, index++, ref interfaceData))
            {
                int requiredSize = 0;
                SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
                if (requiredSize == 0) continue;

                IntPtr detailDataBuffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 5);
                    if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, detailDataBuffer, requiredSize, ref requiredSize, IntPtr.Zero))
                    {
                        IntPtr pDevicePath = new(detailDataBuffer.ToInt64() + 4);
                        string? devicePath = Marshal.PtrToStringAuto(pDevicePath);
                        if (devicePath != null && devicePath.Contains("vid_1532", StringComparison.OrdinalIgnoreCase))
                        {
                            using var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                            if (!handle.IsInvalid)
                            {
                                HIDD_ATTRIBUTES attr = new() { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                                if (HidD_GetAttributes(handle, ref attr))
                                {
                                    if (HidD_GetPreparsedData(handle, out IntPtr ppData))
                                    {
                                        try
                                        {
                                            if (HidP_GetCaps(ppData, out HIDP_CAPS caps) >= 0)
                                            {
                                                // Razer control protocol requires 90 or 91 bytes feature report capability
                                                if (caps.FeatureReportByteLength >= 90)
                                                {
                                                    // Priority 1: primary composite interface MI_00
                                                    if (devicePath.Contains("&mi_00#", StringComparison.OrdinalIgnoreCase) && !devicePath.Contains("col", StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        return (devicePath, attr.ProductID);
                                                    }

                                                    // Priority 2: standalone non-composite mouse (no &mi_ in path)
                                                    if (!devicePath.Contains("&mi_", StringComparison.OrdinalIgnoreCase) && candidateFallback == null)
                                                    {
                                                        candidateFallback = devicePath;
                                                        fallbackPid = attr.ProductID;
                                                    }
                                                    else if (candidateFallback == null)
                                                    {
                                                        candidateFallback = devicePath;
                                                        fallbackPid = attr.ProductID;
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            HidD_FreePreparsedData(ppData);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailDataBuffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return (candidateFallback, fallbackPid);
    }

    public static string ResolveRazerModelName(ushort pid) => pid switch
    {
        0x009F => "Razer Viper Mini Signature Edition (Wireless)",
        0x009E => "Razer Viper Mini Signature Edition (Wired)",
        0x00A5 => "Razer Viper V2 Pro (Wireless)",
        0x00A6 => "Razer Viper V2 Pro (Wired)",
        0x00B6 => "Razer Viper V3 Pro (Wireless)",
        0x00B7 => "Razer Viper V3 Pro (Wired)",
        0x00B5 => "Razer Viper V3 Hyperspeed",
        0x008A => "Razer Viper 8KHz",
        0x0078 => "Razer Viper",
        0x007A => "Razer Viper Ultimate (Dongle)",
        0x007B => "Razer Viper Ultimate (Wireless)",
        0x007C => "Razer Viper Ultimate (Wired)",
        0x008F => "Razer Viper Mini",
        0x00B3 => "Razer DeathAdder V3 Pro (Wireless)",
        0x00B2 => "Razer DeathAdder V3 Pro (Wired)",
        0x00B4 => "Razer DeathAdder V3 Hyperspeed",
        0x0084 => "Razer DeathAdder V2",
        0x0085 => "Razer DeathAdder V2 Pro (Wireless)",
        0x0086 => "Razer DeathAdder V2 Pro (Wired)",
        0x008C => "Razer DeathAdder V2 Mini",
        0x0091 => "Razer DeathAdder V2 X Hyperspeed",
        0x006E => "Razer DeathAdder Essential",
        0x005C => "Razer DeathAdder Elite",
        0x0043 => "Razer DeathAdder Chroma",
        0x0099 => "Razer Basilisk V3",
        0x009A => "Razer Basilisk V3 Pro (Wireless)",
        0x009B => "Razer Basilisk V3 Pro (Wired)",
        0x00A4 => "Razer Basilisk V3 35K",
        0x0088 => "Razer Basilisk Ultimate",
        0x00AB => "Razer Cobra Pro (Wireless)",
        0x00AC => "Razer Cobra Pro (Wired)",
        0x00B0 => "Razer Cobra",
        0x0090 => "Razer Orochi V2",
        0x0067 => "Razer Naga Trinity",
        0x008D => "Razer Naga Pro (Wireless)",
        0x00A1 => "Razer Naga V2 Pro",
        0x00A2 => "Razer Naga V2 Hyperspeed",
        _ => $"Mysz Razer (PID: 0x{pid:X4})"
    };

    private static byte[] CreateRazerReport(byte commandClass, byte commandId, byte dataSize, ReadOnlySpan<byte> args)
    {
        byte[] buf = new byte[91];
        buf[0] = 0x00; // Report ID
        buf[1] = 0x00; // Status
        buf[2] = 0x1F; // Transaction ID
        buf[3] = 0x00;
        buf[4] = 0x00;
        buf[5] = 0x00;
        buf[6] = dataSize;
        buf[7] = commandClass;
        buf[8] = commandId;

        for (int i = 0; i < args.Length && i < 80; i++)
        {
            buf[9 + i] = args[i];
        }

        // Standard XOR checksum over bytes 3..88 (in 91-byte buffer: indices 3..88)
        byte crc = 0;
        for (int i = 3; i <= 88; i++)
        {
            crc ^= buf[i];
        }
        buf[89] = crc;
        buf[90] = 0x00; // Reserved
        return buf;
    }

    public void Dispose()
    {
        _batteryTimer?.Dispose();
        _batteryTimer = null;
        CloseHandle();
    }
}

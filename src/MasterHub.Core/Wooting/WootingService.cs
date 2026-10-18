using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MasterHub.Core.Wooting;

public class WootingService : IWootingService, IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint ERROR_IO_PENDING = 997;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    // Wooting HID Config Usage Pages (z oficjalnego wooting-rgb-sdk / wooting-usb.c)
    private const ushort WootingCfgUsagePageV2 = 0x1337;
    private const ushort WootingCfgUsagePageV3 = 0xFF55;

    // Wooting Feature Command IDs (z wooting_enums.hpp / wooting-usb.h)
    private const byte CmdGetCurrentKeyboardProfileIndex = 11; // 0x0B
    private const byte CmdActivateProfile = 23;                // 0x17
    private const byte CmdRefreshRgbColors = 29;               // 0x1D
    private const byte CmdWootDevResetAll = 32;                // 0x20
    private const byte CmdReloadProfile = 38;                  // 0x26
    private const byte CmdGetProfileMetadata = 55;             // 0x37

    private const byte StatusSuccess = 0x88;

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle hFile, ref NativeOverlapped lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIo(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(SafeFileHandle HidDeviceObject, StringBuilder Buffer, int BufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

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

    private readonly record struct WootingHidDeviceInfo(
        string Path,
        ushort Pid,
        string ProductName,
        bool UsesMultiReportV3,
        ushort InputReportLength);

    private readonly Lock _lock = new();
    private string? _cachedDevicePath;
    private bool _usesMultiReportV3;
    private ushort _inputReportLength = 257;
    private bool _isDeviceConnected;
    private string _connectedDeviceName = "Klawiatura Wooting";
    private int _currentProfileIndex = 0;

    private readonly string[] _profileNames =
    [
        "Cyfrowy (Domyślny)",
        "Analogowy #1",
        "Analogowy #2",
        "Analogowy #3"
    ];

    private readonly List<int> _configuredSlots = [0, 1, 2, 3];

    public bool IsDeviceConnected => _isDeviceConnected;
    public string ConnectedDeviceName => _connectedDeviceName;
    public string DevicePath => _cachedDevicePath ?? "";
    public int CurrentProfileIndex => _currentProfileIndex;
    public IReadOnlyList<string> ProfileNames => _profileNames;
    public int ConfiguredProfileCount => _configuredSlots.Count;

    public string CurrentProfileName => GetProfileName(_currentProfileIndex);

    public string GetProfileName(int profileIndex)
    {
        if (profileIndex >= 0 && profileIndex < _profileNames.Length)
        {
            return _profileNames[profileIndex];
        }
        return $"Profil #{profileIndex}";
    }

    public event Action<int>? ProfileChanged;
    public event Action<string>? LogMessage;

    public WootingService()
    {
        RefreshDevice();
    }

    public bool RefreshDevice()
    {
        lock (_lock)
        {
            var dev = FindWootingControlDevice();
            if (dev.HasValue)
            {
                _cachedDevicePath = dev.Value.Path;
                _usesMultiReportV3 = dev.Value.UsesMultiReportV3;
                _inputReportLength = dev.Value.InputReportLength > 0 ? dev.Value.InputReportLength : (ushort)257;
                _isDeviceConnected = true;
                _connectedDeviceName = !string.IsNullOrWhiteSpace(dev.Value.ProductName)
                    ? dev.Value.ProductName
                    : ResolveWootingModelName(dev.Value.Pid);

                SyncHardwareStateLocked();

                LogMessage?.Invoke($"[WOOTING] ⌨️ Wykryto klawiaturę: {_connectedDeviceName} (Aktywny profil: #{_currentProfileIndex} - {CurrentProfileName})");
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

    private void SyncHardwareStateLocked()
    {
        // 1. Odczytaj nazwy profili zapisanych w pamięci Flash klawiatury (komenda 55 = GetProfileMetadata)
        var configured = new List<int>(4);
        string[] defaults =
        [
            "Cyfrowy (Domyślny)",
            "Analogowy #1",
            "Analogowy #2",
            "Analogowy #3"
        ];

        for (byte slot = 0; slot < 4; slot++)
        {
            if (TrySendCommandLocked(CmdGetProfileMetadata, slot, 0, 0, 0, out byte[]? payload) && payload != null && payload.Length > 0)
            {
                string? parsedName = TryParseProtobufProfileName(payload);
                _profileNames[slot] = !string.IsNullOrWhiteSpace(parsedName) ? parsedName : defaults[slot];
                configured.Add(slot);
            }
            else
            {
                _profileNames[slot] = defaults[slot];
            }
        }

        _configuredSlots.Clear();
        if (configured.Count > 0)
        {
            _configuredSlots.AddRange(configured);
        }
        else
        {
            _configuredSlots.AddRange([0, 1, 2, 3]);
        }

        // 2. Odczytaj aktualnie aktywny profil z klawiatury (komenda 11 = GetCurrentKeyboardProfileIndex)
        if (TryQueryCurrentProfileIndexLocked(out int hwProfileIndex))
        {
            _currentProfileIndex = hwProfileIndex;
        }
    }

    private static string? TryParseProtobufProfileName(byte[] payload)
    {
        // Format Protobuf w firmware Wooting: 0x0A (field 1, string), <strLen>, <UTF-8 bytes...>
        for (int i = 0; i + 1 < payload.Length; i++)
        {
            if (payload[i] == 0x0A)
            {
                int strLen = payload[i + 1];
                if (strLen > 0 && i + 2 + strLen <= payload.Length)
                {
                    string s = Encoding.UTF8.GetString(payload, i + 2, strLen).Trim('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        return s;
                    }
                }
            }
        }
        return null;
    }

    private bool TryQueryCurrentProfileIndexLocked(out int profileIndex)
    {
        profileIndex = _currentProfileIndex;
        if (TrySendCommandLocked(CmdGetCurrentKeyboardProfileIndex, 0, 0, 0, 0, out byte[]? payload) &&
            payload != null &&
            payload.Length >= 1)
        {
            int idx = payload[0];
            if (idx >= 0 && idx <= 3)
            {
                profileIndex = idx;
                return true;
            }
        }
        return false;
    }

    public bool SwitchProfile(int profileIndex)
    {
        if (profileIndex < 0) profileIndex = 0;
        if (profileIndex > 3) profileIndex = 3;

        lock (_lock)
        {
            EnsureDeviceConnectedLocked();
            if (!_isDeviceConnected || string.IsNullOrWhiteSpace(_cachedDevicePath))
            {
                LogMessage?.Invoke($"[WOOTING] Brak podłączonej klawiatury Wooting — nie można przełączyć na profil #{profileIndex}");
                return false;
            }

            // Sekwencja przełączenia profilu sprzętowego Wooting:
            // 1. ReloadProfile (38 / 0x26) — ładuje profil z pamięci Flash
            // 2. ActivateProfile (23 / 0x17) — aktywuje profil (b4 = profileIndex)
            // 3. WootDevResetAll (32 / 0x20) + RefreshRgbColors (29 / 0x1D) — odświeża podświetlenie RGB profilu
            TrySendCommandLocked(CmdReloadProfile, (byte)profileIndex, 0, 0, 0, out _);
            bool ok = TrySendCommandLocked(CmdActivateProfile, (byte)profileIndex, 0, 0, 0, out _);
            if (!ok)
            {
                // Ponów po odświeżeniu ścieżki urządzenia
                if (RefreshDevice())
                {
                    TrySendCommandLocked(CmdReloadProfile, (byte)profileIndex, 0, 0, 0, out _);
                    ok = TrySendCommandLocked(CmdActivateProfile, (byte)profileIndex, 0, 0, 0, out _);
                }
            }

            if (ok)
            {
                TrySendCommandLocked(CmdWootDevResetAll, 0, 0, 0, 0, out _);
                TrySendCommandLocked(CmdRefreshRgbColors, (byte)profileIndex, 0, 0, 0, out _);

                if (TryQueryCurrentProfileIndexLocked(out int verifiedIndex))
                {
                    _currentProfileIndex = verifiedIndex;
                    if (verifiedIndex != profileIndex)
                    {
                        LogMessage?.Invoke($"[WOOTING] ⚠️ Slot profilu #{profileIndex} jest pusty w Wootility — klawiatura pozostała na profilu #{verifiedIndex} ({CurrentProfileName})");
                    }
                    else
                    {
                        LogMessage?.Invoke($"[WOOTING] ⌨️ Przełączono profil klawiatury na: #{_currentProfileIndex} ({CurrentProfileName})");
                    }
                }
                else
                {
                    _currentProfileIndex = profileIndex;
                    LogMessage?.Invoke($"[WOOTING] ⌨️ Przełączono profil klawiatury na: #{_currentProfileIndex} ({CurrentProfileName})");
                }

                ProfileChanged?.Invoke(_currentProfileIndex);
                return true;
            }

            LogMessage?.Invoke($"[WOOTING] Błąd wysyłania komendy przełączania profilu #{profileIndex}");
            return false;
        }
    }

    public int CycleProfile(int direction)
    {
        lock (_lock)
        {
            EnsureDeviceConnectedLocked();

            int step = direction >= 0 ? 1 : -1;
            int nextProfile;

            if (_configuredSlots.Count > 0)
            {
                int currentPos = _configuredSlots.IndexOf(_currentProfileIndex);
                if (currentPos < 0)
                {
                    nextProfile = _configuredSlots[0];
                }
                else
                {
                    int nextPos = (currentPos + step) % _configuredSlots.Count;
                    if (nextPos < 0) nextPos += _configuredSlots.Count;
                    nextProfile = _configuredSlots[nextPos];
                }
            }
            else
            {
                nextProfile = (_currentProfileIndex + step) % 4;
                if (nextProfile < 0) nextProfile += 4;
            }

            SwitchProfile(nextProfile);
            return _currentProfileIndex;
        }
    }

    public int CycleProfileNext()
    {
        return CycleProfile(1);
    }

    private void EnsureDeviceConnectedLocked()
    {
        if (string.IsNullOrWhiteSpace(_cachedDevicePath) || !_isDeviceConnected)
        {
            RefreshDevice();
        }
    }

    private bool TrySendCommandLocked(byte commandId, byte b4, byte b5, byte b6, byte b7, out byte[]? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(_cachedDevicePath))
        {
            return false;
        }

        // Zgodnie z wooting-usb.c:
        // V3 Multi-report (0xFF55): ReportID = 0x01, Magic = 0xD1, 0xDA
        // V2 Single-report (0x1337): ReportID = 0x00, Magic = 0xD0, 0xDA
        byte reportId = _usesMultiReportV3 ? (byte)0x01 : (byte)0x00;
        byte magic1 = _usesMultiReportV3 ? (byte)0xD1 : (byte)0xD0;
        byte[] featureReport = [reportId, magic1, 0xDA, commandId, b4, b5, b6, b7];

        try
        {
            using SafeFileHandle handle = CreateFile(
                _cachedDevicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                // Fallback dla interfejsów bez dostępu GENERIC_READ
                using SafeFileHandle writeOnlyHandle = CreateFile(
                    _cachedDevicePath,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (writeOnlyHandle.IsInvalid)
                {
                    return false;
                }

                return HidD_SetFeature(writeOnlyHandle, featureReport, featureReport.Length);
            }

            if (!HidD_SetFeature(handle, featureReport, featureReport.Length))
            {
                return false;
            }

            int respLen = Math.Max((int)_inputReportLength, 65);
            byte[] resp = new byte[respLen];

            if (TryReadInputReportWithTimeout(handle, resp, 200, out int bytesRead) && bytesRead >= 6)
            {
                // Sprawdź odpowiedź urządzenia: [ReportId, Magic1, 0xDA, CmdId, Status(0x88), PayloadLen, Payload...]
                int offset = (resp[0] == 0xD0 || resp[0] == 0xD1) ? 0 : 1;
                if (offset + 5 <= bytesRead &&
                    resp[offset + 1] == 0xDA &&
                    resp[offset + 2] == commandId &&
                    resp[offset + 3] == StatusSuccess)
                {
                    int payloadLen = resp[offset + 4];
                    int payloadStart = offset + 5;
                    int available = Math.Max(0, Math.Min(payloadLen, bytesRead - payloadStart));
                    payload = new byte[available];
                    if (available > 0)
                    {
                        Buffer.BlockCopy(resp, payloadStart, payload, 0, available);
                    }
                    return true;
                }
            }

            // HidD_SetFeature powiodło się nawet jeśli nie odczytano ramki zwrotnej
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadInputReportWithTimeout(SafeFileHandle handle, byte[] buffer, uint timeoutMs, out int bytesRead)
    {
        bytesRead = 0;
        IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
        if (hEvent == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            NativeOverlapped overlapped = new()
            {
                EventHandle = hEvent
            };

            if (!ReadFile(handle, buffer, (uint)buffer.Length, out uint transferred, ref overlapped))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING)
                {
                    return false;
                }

                uint waitRes = WaitForSingleObject(hEvent, timeoutMs);
                if (waitRes != WAIT_OBJECT_0)
                {
                    CancelIo(handle);
                    return false;
                }

                if (!GetOverlappedResult(handle, ref overlapped, out transferred, false))
                {
                    return false;
                }
            }

            bytesRead = (int)transferred;
            return bytesRead > 0;
        }
        finally
        {
            CloseHandle(hEvent);
        }
    }

    private static WootingHidDeviceInfo? FindWootingControlDevice()
    {
        Guid hidGuid = GUID_DEVINTERFACE_HID;
        IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1)) return null;

        int index = 0;
        SP_DEVICE_INTERFACE_DATA interfaceData = new() { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
        WootingHidDeviceInfo? candidateFallback = null;

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
                    Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, detailDataBuffer, requiredSize, ref requiredSize, IntPtr.Zero))
                    {
                        IntPtr pDevicePath = new(detailDataBuffer.ToInt64() + 4);
                        string? devicePath = Marshal.PtrToStringAuto(pDevicePath);
                        if (devicePath != null &&
                            (devicePath.Contains("vid_31e3", StringComparison.OrdinalIgnoreCase) ||
                             devicePath.Contains("vid_03eb", StringComparison.OrdinalIgnoreCase)))
                        {
                            using var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                            if (!handle.IsInvalid)
                            {
                                HIDD_ATTRIBUTES attr = new() { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                                if (HidD_GetAttributes(handle, ref attr))
                                {
                                    var productBuilder = new StringBuilder(256);
                                    string productName = HidD_GetProductString(handle, productBuilder, 256)
                                        ? productBuilder.ToString().Trim()
                                        : string.Empty;

                                    if (HidD_GetPreparsedData(handle, out IntPtr ppData))
                                    {
                                        try
                                        {
                                            if (HidP_GetCaps(ppData, out HIDP_CAPS caps) >= 0)
                                            {
                                                // Wooting Config Interface V2 (0x1337) lub V3 Multi-Report (0xFF55)
                                                if (caps.UsagePage == WootingCfgUsagePageV2 || caps.UsagePage == WootingCfgUsagePageV3)
                                                {
                                                    return new WootingHidDeviceInfo(
                                                        devicePath,
                                                        attr.ProductID,
                                                        productName,
                                                        caps.UsagePage == WootingCfgUsagePageV3,
                                                        caps.InputReportByteLength);
                                                }

                                                if (caps.FeatureReportByteLength >= 8 && candidateFallback == null)
                                                {
                                                    candidateFallback = new WootingHidDeviceInfo(
                                                        devicePath,
                                                        attr.ProductID,
                                                        productName,
                                                        false,
                                                        caps.InputReportByteLength);
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

        return candidateFallback;
    }

    public static string ResolveWootingModelName(ushort pid) => (pid & 0xFFF0) switch
    {
        0xFF00 when pid == 0xFF01 => "Wooting One",
        0xFF00 when pid == 0xFF02 => "Wooting Two",
        0x1100 => "Wooting One",
        0x1200 => "Wooting Two",
        0x1210 => "Wooting Two Lekker Edition",
        0x1220 => "Wooting Two HE",
        0x1230 or 0x1240 => "Wooting Two HE (ARM)",
        0x1300 => "Wooting 60HE",
        0x1310 => "Wooting 60HE (ARM)",
        0x1320 => "Wooting 60HE+",
        0x1340 => "Wooting 60HE v2",
        0x1400 or 0x1410 => "Wooting UwU / 80HE",
        0x1500 or 0x1510 => "Wooting 80HE / UwU",
        _ => $"Klawiatura Wooting (PID: 0x{pid:X4})"
    };

    public void Dispose()
    {
        // Uchwyt HID jest otwierany wyłącznie na czas transakcji i natychmiast zwalniany.
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using MasterHub.Core.Models;

namespace MasterHub.Core.Native;

/// <summary>
/// Managed wrapper around a Win32 USB HID device handle for Cooler Master MasterHub hardware modules.
/// Provides non-blocking asynchronous input reading and robust feature/output report transmission.
/// </summary>
public sealed class MasterHubDeviceHandle : IDisposable
{
    private const uint ERROR_IO_PENDING = 997;
    private const uint ERROR_OPERATION_ABORTED = 995;
    private const uint ERROR_DEVICE_NOT_CONNECTED = 1167;
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle hFile,
        IntPtr lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ResetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

    private readonly SafeFileHandle _handle;
    private readonly ushort _vendorId;
    private readonly ushort _productId;
    private readonly string _serialNumber;
    private readonly string _devicePath;

    private readonly ConcurrentQueue<byte[]> _inputQueue = new();
    private readonly AutoResetEvent _dataAvailableEvent = new(false);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _readThread;
    private readonly System.Threading.Lock _writeLock = new();
    private readonly IntPtr _writeEvent;
    private IntPtr _readBuffer;
    private IntPtr _readOverlapped;
    private IntPtr _writeBuffer;
    private IntPtr _writeOverlapped;

    private volatile bool _isDisconnected = false;
    private bool _disposed = false;

    public ushort VendorId => _vendorId;
    public ushort ProductId => _productId;
    public string SerialNumber => _serialNumber;
    public string DevicePath => _devicePath;
    public bool IsValid => !_disposed && !_isDisconnected && _handle != null && !_handle.IsInvalid && !_handle.IsClosed;
    public bool IsDisconnected => _isDisconnected;

    public event Action<byte[]>? ReportReceived;

    public MasterHubDeviceHandle(SafeFileHandle handle, ushort vendorId, ushort productId, string serialNumber, string devicePath)
    {
        _handle = handle;
        _vendorId = vendorId;
        _productId = productId;
        _serialNumber = serialNumber;
        _devicePath = devicePath;

        _readBuffer = Marshal.AllocHGlobal(64);
        _readOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        _writeBuffer = Marshal.AllocHGlobal(320);
        _writeOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());

        for (int i = 0; i < 64; i++) Marshal.WriteByte(_readBuffer, i, 0);
        for (int i = 0; i < Marshal.SizeOf<NativeOverlapped>(); i++) Marshal.WriteByte(_readOverlapped, i, 0);
        for (int i = 0; i < 320; i++) Marshal.WriteByte(_writeBuffer, i, 0);
        for (int i = 0; i < Marshal.SizeOf<NativeOverlapped>(); i++) Marshal.WriteByte(_writeOverlapped, i, 0);

        _writeEvent = CreateEvent(IntPtr.Zero, true, false, null);

        _readThread = new Thread(ReadWorker)
        {
            IsBackground = true,
            Name = $"MasterHub-Reader-0x{productId:X4}",
            Priority = ThreadPriority.AboveNormal
        };
        _readThread.Start();
    }

    private void ReadWorker()
    {
        IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
        if (hEvent == IntPtr.Zero) return;

        try
        {
            while (!_cts.Token.IsCancellationRequested && !_disposed && !_handle.IsInvalid && !_handle.IsClosed)
            {
                // Re-zero the overlapped structure and re-arm hEvent before every ReadFile
                for (int i = 0; i < 24; i++) Marshal.WriteByte(_readOverlapped, i, 0);
                Marshal.WriteIntPtr(_readOverlapped, 24, hEvent);
                ResetEvent(hEvent);

                uint bytesRead = 0;
                bool readSuccess = ReadFile(_handle, _readBuffer, 64, out bytesRead, _readOverlapped);

                if (!readSuccess)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == (int)ERROR_IO_PENDING)
                    {
                        while (!_cts.Token.IsCancellationRequested && !_disposed && !_handle.IsClosed)
                        {
                            uint waitRes = WaitForSingleObject(hEvent, 50);
                            if (waitRes == WAIT_OBJECT_0)
                            {
                                if (GetOverlappedResult(_handle, _readOverlapped, out bytesRead, false))
                                {
                                    readSuccess = true;
                                }
                                else
                                {
                                    int err = Marshal.GetLastWin32Error();
                                    if (err == (int)ERROR_DEVICE_NOT_CONNECTED || err == 6 || err == 2)
                                    {
                                        _isDisconnected = true;
                                    }
                                }
                                break;
                            }
                            else if (waitRes == WAIT_TIMEOUT)
                            {
                                continue;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (!readSuccess && (_cts.Token.IsCancellationRequested || _disposed))
                        {
                            CancelIoEx(_handle, _readOverlapped);
                            break;
                        }
                    }
                    else
                    {
                        if (error == (int)ERROR_DEVICE_NOT_CONNECTED || error == 6 || error == 2)
                        {
                            _isDisconnected = true;
                            break;
                        }
                        Thread.Sleep(10);
                        continue;
                    }
                }

                if (readSuccess && bytesRead > 0)
                {
                    byte[] packet = new byte[bytesRead];
                    Marshal.Copy(_readBuffer, packet, 0, (int)bytesRead);

                    while (_inputQueue.Count >= 128)
                    {
                        _inputQueue.TryDequeue(out _);
                    }

                    _inputQueue.Enqueue(packet);
                    _dataAvailableEvent.Set();

                    try
                    {
                        ReportReceived?.Invoke(packet);
                    }
                    catch { }
                }
            }
        }
        catch
        {
            _isDisconnected = true;
        }
        finally
        {
            CloseHandle(hEvent);
        }
    }

    public int ReadInputReport(byte[] buffer, int timeoutMs = 0)
    {
        if (_disposed || _isDisconnected || _handle.IsInvalid || _handle.IsClosed)
        {
            return 7; // Disconnected
        }

        if (_inputQueue.TryDequeue(out var packet))
        {
            int copyLen = Math.Min(buffer.Length, packet.Length);
            Buffer.BlockCopy(packet, 0, buffer, 0, copyLen);
            return 0; // Success
        }

        if (timeoutMs > 0)
        {
            if (_dataAvailableEvent.WaitOne(timeoutMs))
            {
                if (_inputQueue.TryDequeue(out packet))
                {
                    int copyLen = Math.Min(buffer.Length, packet.Length);
                    Buffer.BlockCopy(packet, 0, buffer, 0, copyLen);
                    return 0;
                }
            }
        }

        return 2; // Timeout / No data pending
    }

    public int WriteOutputReport(byte[] report)
    {
        if (_disposed || _isDisconnected || _handle.IsInvalid || _handle.IsClosed)
        {
            return 7;
        }

        lock (_writeLock)
        {
            if (_writeEvent == IntPtr.Zero || _writeBuffer == IntPtr.Zero || _writeOverlapped == IntPtr.Zero) return -1;
            ResetEvent(_writeEvent);

            int writeLen = Math.Min(report.Length, 320);
            Marshal.Copy(report, 0, _writeBuffer, writeLen);

            // Zero out overlapped structure and set event handle
            for (int i = 0; i < Marshal.SizeOf<NativeOverlapped>(); i++) Marshal.WriteByte(_writeOverlapped, i, 0);
            Marshal.WriteIntPtr(_writeOverlapped, IntPtr.Size * 2 + 8, _writeEvent);

            uint written = 0;
            bool ok = WriteFile(_handle, _writeBuffer, (uint)writeLen, out written, _writeOverlapped);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == (int)ERROR_IO_PENDING)
                {
                    uint wait = WaitForSingleObject(_writeEvent, 2000);
                    if (wait == WAIT_OBJECT_0)
                    {
                        ok = GetOverlappedResult(_handle, _writeOverlapped, out written, false);
                    }
                    else
                    {
                        CancelIoEx(_handle, _writeOverlapped);
                        GetOverlappedResult(_handle, _writeOverlapped, out _, true);
                        return 2; // Timeout
                    }
                }
                else
                {
                    if (err == (int)ERROR_DEVICE_NOT_CONNECTED || err == 6 || err == 2)
                    {
                        _isDisconnected = true;
                    }
                    return err;
                }
            }

            return ok && written == writeLen ? 0 : -1;
        }
    }

    public int SetFeatureReport(byte[] report)
    {
        if (_disposed || _isDisconnected || _handle.IsInvalid || _handle.IsClosed)
        {
            return 7;
        }

        lock (_writeLock)
        {
            bool ok = HidD_SetFeature(_handle, report, report.Length);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == (int)ERROR_DEVICE_NOT_CONNECTED || err == 6 || err == 2)
                {
                    _isDisconnected = true;
                }
                return err;
            }
            return 0;
        }
    }

    public int GetFeatureReport(byte[] report)
    {
        if (_disposed || _isDisconnected || _handle.IsInvalid || _handle.IsClosed)
        {
            return 7;
        }

        lock (_writeLock)
        {
            bool ok = HidD_GetFeature(_handle, report, report.Length);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == (int)ERROR_DEVICE_NOT_CONNECTED || err == 6 || err == 2)
                {
                    _isDisconnected = true;
                }
                return err;
            }
            return 0;
        }
    }

    // High-Level Key Deck 15 (M4315) Protocol Helpers
    public int SetSwMode(bool isSoftwareMode)
    {
        byte[] rep = new byte[32];
        rep[0] = 0x03;
        rep[1] = 0x3a;
        rep[2] = (byte)(isSoftwareMode ? 0x01 : 0x00);
        return SetFeatureReport(rep);
    }

    public int AdjustBacklight(byte brightness)
    {
        byte[] rep = new byte[32];
        rep[0] = 0x03;
        rep[1] = 0x34;
        rep[2] = Math.Clamp(brightness, (byte)0, (byte)100);
        return SetFeatureReport(rep);
    }

    public int DisplayColorOnButton(byte buttonId, byte r, byte g, byte b)
    {
        byte[] rep = new byte[32];
        rep[0] = 0x03;
        rep[1] = 0x37;
        rep[2] = buttonId;
        rep[3] = r;
        rep[4] = g;
        rep[5] = b;
        return SetFeatureReport(rep);
    }

    public int DisplayPictureOnButton(byte buttonId, byte[] jpegBuf, int bufLen)
    {
        const int ReportLen = 320;
        const int ChunkMax = 312;

        int total = Math.Min(jpegBuf.Length, bufLen);
        int remaining = total;
        ushort chunkIndex = 0;

        byte[] report = new byte[ReportLen];
        report[0] = 0x02;
        report[1] = 0x36;
        report[6] = buttonId;

        while (remaining > 0)
        {
            int chunkLen = Math.Min(remaining, ChunkMax);
            byte isLast = (byte)(remaining <= ChunkMax ? 1 : 0);

            report[2] = (byte)(chunkIndex & 0xFF);
            report[3] = (byte)((chunkIndex >> 8) & 0xFF);
            report[4] = (byte)(chunkLen & 0xFF);
            report[5] = (byte)((chunkLen >> 8) & 0xFF);
            report[7] = isLast;

            int srcOffset = total - remaining;
            Buffer.BlockCopy(jpegBuf, srcOffset, report, 8, chunkLen);

            if (chunkLen < ChunkMax)
            {
                Array.Clear(report, 8 + chunkLen, ReportLen - (8 + chunkLen));
            }

            int res = WriteOutputReport(report);
            if (res != 0) return res;

            chunkIndex++;
            remaining -= chunkLen;
        }

        return 0;
    }

    // High-Level M0 (Base Station, Fader, Roller) Protocol Helpers
    public int AdjustLedMode(byte deviceType, byte ledIndex, byte mode, bool saveFw)
    {
        byte[] rep = new byte[64];
        rep[0] = 0x02;
        rep[1] = 0x2c;
        rep[2] = deviceType;
        rep[3] = ledIndex;
        rep[4] = mode;
        rep[5] = (byte)(saveFw ? 1 : 0);
        return WriteOutputReport(rep);
    }

    public int AdjustLedColor(byte deviceType, byte ledIndex, byte r, byte g, byte b, bool saveFw)
    {
        byte[] rep = new byte[64];
        rep[0] = 0x02;
        rep[1] = 0x2d;
        rep[2] = deviceType;
        rep[3] = ledIndex;
        rep[4] = r;
        rep[5] = g;
        rep[6] = b;
        rep[7] = (byte)(saveFw ? 1 : 0);
        return WriteOutputReport(rep);
    }

    public int AdjustLedBrightness(byte deviceType, byte ledIndex, byte brightness)
    {
        byte[] rep = new byte[64];
        rep[0] = 0x02;
        rep[1] = 0x2e;
        rep[2] = deviceType;
        rep[3] = ledIndex;
        rep[4] = Math.Clamp(brightness, (byte)0, (byte)100);
        return WriteOutputReport(rep);
    }

    public int AdjustLedSpeed(byte speed)
    {
        byte[] rep = new byte[64];
        rep[0] = 0x02;
        rep[1] = 0x30;
        rep[2] = Math.Clamp(speed, (byte)0, (byte)100);
        return WriteOutputReport(rep);
    }

    public int GetSliderValues(byte[] buf)
    {
        byte[] rep = new byte[32];
        rep[0] = 0x33;
        int res = GetFeatureReport(rep);
        if (res == 0 && buf != null)
        {
            Buffer.BlockCopy(rep, 2, buf, 0, Math.Min(buf.Length, 5));
        }
        return res;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();
            _dataAvailableEvent.Set();
            if (_handle != null && !_handle.IsInvalid && !_handle.IsClosed)
            {
                CancelIoEx(_handle, IntPtr.Zero);
            }
            _readThread.Join(500);
        }
        catch { }

        if (_writeEvent != IntPtr.Zero)
        {
            CloseHandle(_writeEvent);
        }

        if (_readBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(_readBuffer); _readBuffer = IntPtr.Zero; }
        if (_readOverlapped != IntPtr.Zero) { Marshal.FreeHGlobal(_readOverlapped); _readOverlapped = IntPtr.Zero; }
        if (_writeBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(_writeBuffer); _writeBuffer = IntPtr.Zero; }
        if (_writeOverlapped != IntPtr.Zero) { Marshal.FreeHGlobal(_writeOverlapped); _writeOverlapped = IntPtr.Zero; }

        _handle?.Dispose();
        _cts.Dispose();
        _dataAvailableEvent.Dispose();
    }
}

/// <summary>
/// Native Win32 USB HID transport for discovering and managing Cooler Master MasterHub modular devices.
/// Completely open-source, eliminating any need for vendor DLLs or installation paths.
/// </summary>
public static class MasterHubUsbTransport
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_DEVICEINTERFACE = 0x00000010;

    private static readonly Guid GUID_DEVINTERFACE_HID = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        int MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        int DeviceInterfaceDetailDataSize,
        ref int RequiredSize,
        IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetSerialNumberString(SafeFileHandle HidDeviceObject, StringBuilder Buffer, int BufferLength);

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

    public static MasterHubDeviceHandle? ConnectDevice(ushort vid, ushort pid, string? serialNumber = null)
    {
        Guid hidGuid = GUID_DEVINTERFACE_HID;
        IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1)) return null;

        int index = 0;
        SP_DEVICE_INTERFACE_DATA interfaceData = new() { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };

        try
        {
            while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, index++, ref interfaceData))
            {
                int requiredSize = 0;
                SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
                if (requiredSize == 0) continue;

                IntPtr detailBuffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, detailBuffer, requiredSize, ref requiredSize, IntPtr.Zero))
                    {
                        IntPtr pPath = new(detailBuffer.ToInt64() + 4);
                        string? devicePath = Marshal.PtrToStringAuto(pPath);
                        if (devicePath == null) continue;

                        string vidStr = $"vid_{vid:x4}";
                        string pidStr = $"pid_{pid:x4}";
                        if (!devicePath.Contains(vidStr, StringComparison.OrdinalIgnoreCase) ||
                            !devicePath.Contains(pidStr, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var handle = CreateFile(
                            devicePath,
                            GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            OPEN_EXISTING,
                            FILE_FLAG_OVERLAPPED,
                            IntPtr.Zero);

                        if (handle.IsInvalid) continue;

                        HIDD_ATTRIBUTES attr = new() { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                        if (!HidD_GetAttributes(handle, ref attr) || attr.VendorID != vid || attr.ProductID != pid)
                        {
                            handle.Dispose();
                            continue;
                        }

                        StringBuilder sb = new(256);
                        string devSerial = HidD_GetSerialNumberString(handle, sb, 256) ? sb.ToString().Trim() : string.Empty;

                        if (!string.IsNullOrEmpty(serialNumber))
                        {
                            bool serialMatches = !string.IsNullOrEmpty(devSerial) && string.Equals(devSerial, serialNumber, StringComparison.OrdinalIgnoreCase);
                            bool pathMatches = devicePath.Contains(serialNumber, StringComparison.OrdinalIgnoreCase);
                            if (!serialMatches && !pathMatches)
                            {
                                handle.Dispose();
                                continue;
                            }
                        }

                        return new MasterHubDeviceHandle(handle, vid, pid, devSerial, devicePath);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return null;
    }

    public static List<DeviceInfo> ScanDevices()
    {
        var list = new List<DeviceInfo>();
        Guid hidGuid = GUID_DEVINTERFACE_HID;
        IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1)) return list;

        int index = 0;
        SP_DEVICE_INTERFACE_DATA interfaceData = new() { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
        var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, index++, ref interfaceData))
            {
                int requiredSize = 0;
                SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
                if (requiredSize == 0) continue;

                IntPtr detailBuffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, detailBuffer, requiredSize, ref requiredSize, IntPtr.Zero))
                    {
                        IntPtr pPath = new(detailBuffer.ToInt64() + 4);
                        string? devicePath = Marshal.PtrToStringAuto(pPath);
                        if (devicePath == null || !devicePath.Contains("vid_2516", StringComparison.OrdinalIgnoreCase)) continue;

                        using var handle = CreateFile(
                            devicePath,
                            GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            OPEN_EXISTING,
                            FILE_FLAG_OVERLAPPED,
                            IntPtr.Zero);

                        if (handle.IsInvalid) continue;

                        HIDD_ATTRIBUTES attr = new() { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                        if (!HidD_GetAttributes(handle, ref attr) || attr.VendorID != 0x2516) continue;

                        StringBuilder sb = new(256);
                        string devSerial = HidD_GetSerialNumberString(handle, sb, 256) ? sb.ToString().Trim() : string.Empty;

                        string key = $"{attr.ProductID}_{devSerial}";
                        if (!seenSerials.Add(key)) continue;

                        var info = new DeviceInfo
                        {
                            VendorId = 0x2516,
                            ProductId = attr.ProductID,
                            SerialNumber = devSerial
                        };

                        switch (attr.ProductID)
                        {
                            case 0x01FE:
                                info.ModuleType = MasterHubModuleType.KeyDeck15;
                                info.Name = "MasterHub Key Deck (15 Klawiszy LCD)";
                                break;
                            case 0x0200:
                                info.ModuleType = MasterHubModuleType.Fader5;
                                info.Name = "MasterHub Fader (5 Suwaków)";
                                break;
                            case 0x0204:
                                info.ModuleType = MasterHubModuleType.RollerDial;
                                info.Name = "MasterHub Roller (2 Rolki / Pokrętła)";
                                break;
                            case 0x01F6:
                                info.ModuleType = MasterHubModuleType.BaseStation;
                                info.Name = "MasterHub Base (Stacja Bazowa)";
                                break;
                            case 0x131E:
                            case 0x1312:
                                info.ModuleType = MasterHubModuleType.ScreenDisplay;
                                info.Name = "MasterHub Display (Ekran IPS)";
                                break;
                            default:
                                info.ModuleType = MasterHubModuleType.Unknown;
                                info.Name = $"MasterHub Urządzenie (PID 0x{attr.ProductID:X4})";
                                break;
                        }

                        list.Add(info);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return list;
    }
}

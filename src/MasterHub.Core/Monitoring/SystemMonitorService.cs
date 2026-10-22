using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MasterHub.Core.Monitoring;

/// <summary>
/// Serwis monitorujący rzeczywiste zasoby sprzętowe w czasie rzeczywistym na platformie Windows.
/// Wykorzystuje natywne interfejsy sprzętowe:
/// - DXGI (dxgi.dll) do wykrywania wszystkich fizycznych kart graficznych (NVIDIA / AMD / Intel) i ich identyfikatorów LUID
/// - NVML (nvml.dll) dla kart NVIDIA GeForce / RTX (temperatura rdzenia °C, obciążenie GPU %)
/// - AMD ADL2 (atiadlxx.dll) dla kart AMD Radeon oraz procesorów AMD Ryzen (temperatura GPU/SoC/CPU °C, obciążenie %, FrameMetrics)
/// - Win32 PDH (pdh.dll) dla liczników GPU Engine (dopasowanych po LUID karty oraz PID procesu 3D)
/// - Win32 GlobalMemoryStatusEx, GetSystemTimes oraz PerformanceCounter (Processor Information, PhysicalDisk)
/// - RTSS Shared Memory (RTSSSharedMemoryV2) oraz detekcję aktywnego procesu 3D dla licznika FPS (0 FPS na pulpicie).
/// </summary>
public sealed class SystemMonitorService : ISystemMonitorService
{
    private readonly object _syncRoot = new();
    private Timer? _timer;
    private bool _isEnabled = true;
    private int _refreshIntervalMs = 1000;
    private bool _disposed;

    // Lista wykrytych kart graficznych
    private List<GpuDeviceInfo> _availableGpus = new();
    private List<string> _availableGpuNames = new();
    private int _selectedGpuIndex;
    private readonly Dictionary<int, (float Load, float Temp)> _perGpuMetrics = new();
    private List<string> _availableDrives = new();

    // Metryki rzeczywiste
    private float _cpuUsagePercent;
    private float _ramUsageMb;
    private float _ramTotalMb;
    private float _ramUsagePercent;
    private float _gpuUsagePercent;
    private float _cpuTemperatureCelsius;
    private float _gpuTemperatureCelsius;
    private float _networkDownloadMbps;
    private float _networkUploadMbps;
    private double _networkDownloadBytesPerSec;
    private double _networkUploadBytesPerSec;
    private float _diskUsagePercent;
    private float _diskReadMbps;
    private float _diskWriteMbps;
    private float _currentFps;

    // Stan CPU dla GetSystemTimes
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private DateTime _lastCpuSampleTime = DateTime.MinValue;
    private bool _hasPreviousCpuSample;

    // Stan Sieci
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTime _lastNetworkSampleTime = DateTime.UtcNow;

    // Stan NVML (NVIDIA)
    private bool _nvmlInitialized;
    private bool _nvmlAvailable = true;
    private readonly List<(IntPtr Handle, string Name)> _nvmlDevices = new();

    // Stan AMD ADL2 (AMD Radeon + AMD Ryzen SoC/CPU Telemetry)
    private static readonly ADL_Main_Memory_AllocDelegate s_adlMemoryAlloc = size => Marshal.AllocHGlobal(size);
    private IntPtr _adlContext = IntPtr.Zero;
    private IntPtr _adlPmLogBuffer = IntPtr.Zero;
    private bool _adlAvailable = true;
    private readonly List<(int AdapterIndex, string Name, int BusNumber)> _adlAdapters = new();
    private bool _adlFrameMetricsStarted;

    // Stan Win32 PDH (CPU Utility, PhysicalDisk Read/Write, GPU Engine *engtype_3D)
    private IntPtr _pdhQuery = IntPtr.Zero;
    private IntPtr _pdhCpuUtilityCounter = IntPtr.Zero;
    private IntPtr _pdhDiskReadCounter = IntPtr.Zero;
    private IntPtr _pdhDiskWriteCounter = IntPtr.Zero;
    private IntPtr _pdhGpu3DCounter = IntPtr.Zero;
    private bool _pdhAvailable = true;

    // Wygładzanie FPS dla aktywnej gry 3D
    private float _smoothedFps;

    // Procesy systemowe/pulpitowe, które NIE są grami 3D (dla licznika FPS = 0 na pulpicie)
    private static readonly HashSet<string> s_nonGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "SearchHost", "SearchApp", "ShellExperienceHost",
        "StartMenuExperienceHost", "ApplicationFrameHost", "SystemSettings", "TextInputHost",
        "Taskmgr", "LockApp", "Widgets", "WidgetBoard", "WidgetService", "SecurityHealthSystray", "SecurityHealthService",
        "RuntimeBroker", "svchost", "sihost", "ShellHost", "dasHost", "fontdrvhost", "conhost", "ctfmon", "SearchIndexer",
        "MasterHub.Avalonia", "MasterHub.App", "MasterHub.Test", "MasterHub", "DevHub",
        "firefox", "chrome", "msedge", "msedgewebview2", "opera", "brave", "vivaldi", "waterfox",
        "discord", "DiscordPTB", "spotify", "steamwebhelper", "steam", "steamservice", "EpicGamesLauncher", "Battle.net", "GalaxyClient",
        "Code", "devenv", "rider64", "WindowsTerminal", "powershell", "pwsh", "cmd", "notepad",
        "nvcontainer", "NVDisplay.Container", "NVIDIA Overlay", "NVIDIA Share", "RadeonSoftware", "AMDRSServ",
        "qbittorrent", "MHDaemon", "GCC", "AorusLcdService", "Antigravity", "vgtray",
        "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "obs64", "obs32", "overwolf", "OverwolfBrowser", "CrosshairX", "CrosshairV2", "MSIAfterburner"
    };

    public bool IsEnabled
    {
        get
        {
            lock (_syncRoot) return _isEnabled;
        }
        set
        {
            lock (_syncRoot)
            {
                _isEnabled = value;
                if (!value)
                {
                    _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
                else
                {
                    _timer?.Change(0, _refreshIntervalMs);
                }
            }
        }
    }

    public int RefreshIntervalMs
    {
        get
        {
            lock (_syncRoot) return _refreshIntervalMs;
        }
        set
        {
            lock (_syncRoot)
            {
                _refreshIntervalMs = Math.Max(200, value);
                _timer?.Change(0, _refreshIntervalMs);
            }
        }
    }

    public IReadOnlyList<GpuDeviceInfo> AvailableGpus
    {
        get
        {
            lock (_syncRoot) return _availableGpus.ToList();
        }
    }

    public IReadOnlyList<string> AvailableGpuNames
    {
        get
        {
            lock (_syncRoot) return _availableGpuNames.ToList();
        }
    }

    public int SelectedGpuIndex
    {
        get
        {
            lock (_syncRoot) return _selectedGpuIndex;
        }
        set
        {
            bool changed = false;
            lock (_syncRoot)
            {
                if (_availableGpus.Count == 0)
                {
                    _selectedGpuIndex = 0;
                }
                else
                {
                    int clamped = Math.Clamp(value, 0, _availableGpus.Count - 1);
                    if (_selectedGpuIndex != clamped)
                    {
                        _selectedGpuIndex = clamped;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                SampleMetricsInternal();
            }
        }
    }

    public string SelectedGpuName
    {
        get
        {
            lock (_syncRoot)
            {
                if (_selectedGpuIndex >= 0 && _selectedGpuIndex < _availableGpus.Count)
                {
                    return _availableGpus[_selectedGpuIndex].Name;
                }
                return "Brak wykrytego GPU";
            }
        }
    }

    public float CpuUsagePercent { get { lock (_syncRoot) return _cpuUsagePercent; } }
    public float RamUsageMb { get { lock (_syncRoot) return _ramUsageMb; } }
    public float RamTotalMb { get { lock (_syncRoot) return _ramTotalMb; } }
    public float RamUsagePercent { get { lock (_syncRoot) return _ramUsagePercent; } }
    public float GpuUsagePercent { get { lock (_syncRoot) return _gpuUsagePercent; } }
    public float CpuTemperatureCelsius { get { lock (_syncRoot) return _cpuTemperatureCelsius; } }
    public float GpuTemperatureCelsius { get { lock (_syncRoot) return _gpuTemperatureCelsius; } }

    public float GetGpuUsagePercent(int gpuIndex)
    {
        lock (_syncRoot)
        {
            if (_perGpuMetrics.TryGetValue(gpuIndex, out var m))
            {
                return m.Load;
            }
            return _gpuUsagePercent;
        }
    }

    public float GetGpuTemperatureCelsius(int gpuIndex)
    {
        lock (_syncRoot)
        {
            if (_perGpuMetrics.TryGetValue(gpuIndex, out var m))
            {
                return m.Temp;
            }
            return _gpuTemperatureCelsius;
        }
    }
    public float NetworkDownloadMbps { get { lock (_syncRoot) return _networkDownloadMbps; } }
    public float NetworkUploadMbps { get { lock (_syncRoot) return _networkUploadMbps; } }
    public double NetworkDownloadBytesPerSec { get { lock (_syncRoot) return _networkDownloadBytesPerSec; } }
    public double NetworkUploadBytesPerSec { get { lock (_syncRoot) return _networkUploadBytesPerSec; } }
    public float DiskUsagePercent { get { lock (_syncRoot) return _diskUsagePercent; } }
    public IReadOnlyList<string> AvailableDrives { get { lock (_syncRoot) return _availableDrives; } }

    public float GetDiskUsagePercent(string? driveName = null)
    {
        try
        {
            string targetRoot = ResolveDriveRoot(driveName);
            var drive = new DriveInfo(targetRoot);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                return (float)((drive.TotalSize - drive.TotalFreeSpace) * 100.0 / drive.TotalSize);
            }
        }
        catch { }
        return _diskUsagePercent;
    }

    private static string ResolveDriveRoot(string? driveName)
    {
        if (string.IsNullOrWhiteSpace(driveName))
        {
            return Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        }
        string trimmed = driveName.Trim();
        if (trimmed.Length >= 2 && trimmed[1] == ':')
        {
            return trimmed.Substring(0, 2) + "\\";
        }
        return Path.GetPathRoot(trimmed) ?? "C:\\";
    }

    public void RefreshDriveList()
    {
        try
        {
            var list = new List<string>();
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.IsReady && d.TotalSize > 0)
                {
                    string root = d.RootDirectory.FullName;
                    string label = !string.IsNullOrWhiteSpace(d.VolumeLabel) ? $" ({d.VolumeLabel})" : "";
                    double totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    list.Add($"{root}{label} [{totalGb:0} GB]");
                }
            }
            lock (_syncRoot)
            {
                _availableDrives = list;
            }
        }
        catch { }
    }

    public float DiskReadMbps { get { lock (_syncRoot) return _diskReadMbps; } }
    public float DiskWriteMbps { get { lock (_syncRoot) return _diskWriteMbps; } }
    public float CurrentFps { get { lock (_syncRoot) return _currentFps; } }

    public event Action? MetricsUpdated;

    public SystemMonitorService()
    {
        InitializeHardwareSubsystems();
        RefreshGpuList();
        RefreshDriveList();
        InitializeBaseline();
        _timer = new Timer(OnTimerTick, null, 250, _refreshIntervalMs);
    }

    private void InitializeHardwareSubsystems()
    {
        // 1. Inicjalizacja NVML (nvml.dll) dla kart NVIDIA
        TryInitializeNvml();

        // 2. Inicjalizacja AMD ADL2 (atiadlxx.dll) dla kart AMD Radeon oraz telemetrii AMD Ryzen
        TryInitializeAdl();

        // 3. Inicjalizacja zapytania Win32 PDH dla CPU Utility, PhysicalDisk Read/Write oraz silników 3D GPU
        TryInitializePdh();
    }

    private void TryInitializeNvml()
    {
        if (!_nvmlAvailable) return;
        try
        {
            if (nvmlInit_v2() == 0)
            {
                _nvmlInitialized = true;
                _nvmlDevices.Clear();
                if (nvmlDeviceGetCount_v2(out uint count) == 0)
                {
                    for (uint i = 0; i < count; i++)
                    {
                        if (nvmlDeviceGetHandleByIndex_v2(i, out IntPtr dev) == 0 && dev != IntPtr.Zero)
                        {
                            var sb = new StringBuilder(128);
                            string name = nvmlDeviceGetName(dev, sb, 128) == 0 ? sb.ToString().Trim() : $"NVIDIA GPU {i}";
                            _nvmlDevices.Add((dev, name));
                        }
                    }
                }
            }
            else
            {
                _nvmlAvailable = false;
            }
        }
        catch
        {
            _nvmlAvailable = false;
        }
    }

    private void TryInitializeAdl()
    {
        if (!_adlAvailable) return;
        try
        {
            if (ADL2_Main_Control_Create(s_adlMemoryAlloc, 1, out _adlContext) == 0 && _adlContext != IntPtr.Zero)
            {
                if (_adlPmLogBuffer == IntPtr.Zero)
                {
                    _adlPmLogBuffer = Marshal.AllocHGlobal(8192);
                }

                _adlAdapters.Clear();
                if (ADL2_Adapter_NumberOfAdapters_Get(_adlContext, out int numAdapters) == 0 && numAdapters > 0)
                {
                    int structSize = Marshal.SizeOf<ADLAdapterInfo>();
                    int totalSize = structSize * numAdapters;
                    IntPtr infoBuffer = Marshal.AllocHGlobal(totalSize);
                    try
                    {
                        for (int b = 0; b < totalSize; b++) Marshal.WriteByte(infoBuffer, b, 0);
                        if (ADL2_Adapter_AdapterInfo_Get(_adlContext, infoBuffer, totalSize) == 0)
                        {
                            var seenBuses = new HashSet<int>();
                            for (int i = 0; i < numAdapters; i++)
                            {
                                IntPtr itemPtr = IntPtr.Add(infoBuffer, i * structSize);
                                var info = Marshal.PtrToStructure<ADLAdapterInfo>(itemPtr);
                                if (info.iPresent != 0 && !string.IsNullOrWhiteSpace(info.strAdapterName))
                                {
                                    if (seenBuses.Add(info.iBusNumber))
                                    {
                                        _adlAdapters.Add((info.iAdapterIndex, info.strAdapterName.Trim(), info.iBusNumber));
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(infoBuffer);
                    }
                }
            }
            else
            {
                _adlAvailable = false;
            }
        }
        catch
        {
            _adlAvailable = false;
        }
    }

    private void TryInitializePdh()
    {
        if (!_pdhAvailable) return;
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _pdhQuery) == 0 && _pdhQuery != IntPtr.Zero)
            {
                PdhAddEnglishCounter(_pdhQuery, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _pdhCpuUtilityCounter);
                PdhAddEnglishCounter(_pdhQuery, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out _pdhDiskReadCounter);
                PdhAddEnglishCounter(_pdhQuery, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out _pdhDiskWriteCounter);
                PdhAddEnglishCounter(_pdhQuery, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _pdhGpu3DCounter);
                PdhCollectQueryData(_pdhQuery);
            }
            else
            {
                _pdhAvailable = false;
            }
        }
        catch
        {
            _pdhAvailable = false;
        }
    }

    public void RefreshGpuList()
    {
        var detected = new List<GpuDeviceInfo>();

        // 1. Enumeracja fizycznych adapterów przez DXGI (CreateDXGIFactory1 -> EnumAdapters1)
        try
        {
            Guid factoryGuid = new("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(ref factoryGuid, out IDXGIFactory1? factory) == 0 && factory != null)
            {
                try
                {
                    uint i = 0;
                    var seenNamesAndLuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    while (factory.EnumAdapters1(i, out IDXGIAdapter1? adapter) == 0 && adapter != null)
                    {
                        try
                        {
                            if (adapter.GetDesc1(out DXGI_ADAPTER_DESC1 desc) == 0)
                            {
                                // Odfiltruj adaptery programowe (DXGI_ADAPTER_FLAG_SOFTWARE == 2, np. Microsoft Basic Render Driver)
                                bool isSoftware = (desc.Flags & 2u) != 0 ||
                                                  desc.VendorId == 0x1414 ||
                                                  desc.Description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase);
                                if (!isSoftware && !string.IsNullOrWhiteSpace(desc.Description))
                                {
                                    string cleanName = desc.Description.Trim();
                                    string key = $"{cleanName}_{desc.AdapterLuidHigh:X8}_{desc.AdapterLuidLow:X8}";
                                    if (seenNamesAndLuids.Add(key))
                                    {
                                        ulong vramMb = (ulong)desc.DedicatedVideoMemory / (1024ul * 1024ul);
                                        detected.Add(new GpuDeviceInfo(
                                            detected.Count,
                                            cleanName,
                                            desc.VendorId,
                                            vramMb,
                                            desc.AdapterLuidLow,
                                            desc.AdapterLuidHigh));
                                    }
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(adapter);
                        }
                        i++;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
        }
        catch { }

        // 2. Jeśli NVML wykrył kartę NVIDIA, której z jakiegoś powodu nie było w DXGI
        if (detected.Count == 0 && _nvmlInitialized && _nvmlDevices.Count > 0)
        {
            for (int i = 0; i < _nvmlDevices.Count; i++)
            {
                detected.Add(new GpuDeviceInfo(i, _nvmlDevices[i].Name, 0x10DE, 0, 0, 0));
            }
        }

        // 3. Sortowanie: dedykowane karty graficzne (największy VRAM / NVIDIA / dedykowane AMD) na pierwszym miejscu
        if (detected.Count > 1)
        {
            detected = detected
                .OrderByDescending(g => g.DedicatedVramMb)
                .ThenByDescending(g => g.VendorId == 0x10DE ? 1 : 0)
                .Select((g, idx) => g with { Index = idx })
                .ToList();
        }

        lock (_syncRoot)
        {
            string? previousSelectedName = (_selectedGpuIndex >= 0 && _selectedGpuIndex < _availableGpus.Count)
                ? _availableGpus[_selectedGpuIndex].Name
                : null;

            _availableGpus = detected;
            _availableGpuNames = detected.Select(g => g.DisplayLabel).ToList();

            if (previousSelectedName != null)
            {
                int foundIdx = _availableGpus.FindIndex(g => string.Equals(g.Name, previousSelectedName, StringComparison.OrdinalIgnoreCase));
                _selectedGpuIndex = foundIdx >= 0 ? foundIdx : 0;
            }
            else
            {
                _selectedGpuIndex = 0;
            }
        }
    }

    private void InitializeBaseline()
    {
        try
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                _lastIdle = idle.ToULong();
                _lastKernel = kernel.ToULong();
                _lastUser = user.ToULong();
                _lastCpuSampleTime = DateTime.UtcNow;
                _hasPreviousCpuSample = true;
            }

            SampleNetworkCounters(out _lastBytesReceived, out _lastBytesSent);
            _lastNetworkSampleTime = DateTime.UtcNow;

            SampleMetricsInternal();
        }
        catch { }
    }

    public void RefreshNow()
    {
        SampleMetricsInternal();
    }

    private void OnTimerTick(object? state)
    {
        lock (_syncRoot)
        {
            if (!_isEnabled || _disposed) return;
        }

        SampleMetricsInternal();
    }

    private void SampleMetricsInternal()
    {
        try
        {
            // 1. RAM Usage via Win32 GlobalMemoryStatusEx (poprawna struktura pamięci fizycznej)
            float sampledRamMb = _ramUsageMb;
            float sampledRamTotal = _ramTotalMb;
            float sampledRamPercent = _ramUsagePercent;

            var memStatus = new MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
            };
            if (GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
            {
                sampledRamTotal = (float)(memStatus.ullTotalPhys / (1024.0 * 1024.0));
                sampledRamMb = (float)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0));
                sampledRamPercent = (float)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) * 100.0 / memStatus.ullTotalPhys);
            }

            // 3. Prędkość sieci (Mb/s oraz bajty/s)
            float sampledDown = _networkDownloadMbps;
            float sampledUp = _networkUploadMbps;
            double sampledBytesDown = _networkDownloadBytesPerSec;
            double sampledBytesUp = _networkUploadBytesPerSec;
            DateTime now = DateTime.UtcNow;
            double elapsedSeconds = (now - _lastNetworkSampleTime).TotalSeconds;

            if (elapsedSeconds >= 0.2 && SampleNetworkCounters(out long curBytesRecv, out long curBytesSent))
            {
                long diffRecv = Math.Max(0, curBytesRecv - _lastBytesReceived);
                long diffSent = Math.Max(0, curBytesSent - _lastBytesSent);

                sampledBytesDown = diffRecv / elapsedSeconds;
                sampledBytesUp = diffSent / elapsedSeconds;
                sampledDown = (float)((sampledBytesDown * 8.0) / 1_000_000.0);
                sampledUp = (float)((sampledBytesUp * 8.0) / 1_000_000.0);

                _lastBytesReceived = curBytesRecv;
                _lastBytesSent = curBytesSent;
                _lastNetworkSampleTime = now;
            }

            // 4. Zajętość dysku systemowego % oraz rzeczywista prędkość odczytu/zapisu (MB/s)
            float sampledDiskPercent = GetDiskUsagePercent(null);

            // 4. Pobranie wszystkich liczników Win32 PDH w jednym wywołaniu PdhCollectQueryData
            SamplePdhCounters(
                out float pdhCpuUtility,
                out float sampledDiskReadMb,
                out float sampledDiskWriteMb,
                out Dictionary<string, float> luidLoads,
                out Dictionary<uint, float> pid3DLoads);

            // 5. CPU Usage % (Rzeczywisty odczyt: PDH % Processor Utility lub Win32 GetSystemTimes)
            float sampledCpu = SampleCpuUsage(pdhCpuUtility);

            // 6. Rzeczywiste obciążenie i temperatura kart graficznych GPU
            var currentPerGpu = new Dictionary<int, (float Load, float Temp)>();
            List<GpuDeviceInfo> gpuList;
            int selIdx;
            lock (_syncRoot)
            {
                gpuList = _availableGpus.ToList();
                selIdx = _selectedGpuIndex;
            }

            float selectedGpuLoad = 0f;
            float selectedGpuTemp = 0f;

            if (gpuList.Count > 0)
            {
                for (int i = 0; i < gpuList.Count; i++)
                {
                    SampleSelectedGpuMetrics(gpuList[i], luidLoads, out float gLoad, out float gTemp);
                    float roundedLoad = MathF.Round(Math.Clamp(gLoad, 0f, 100f), 1);
                    float roundedTemp = MathF.Round(Math.Clamp(gTemp, 0f, 120f), 1);
                    currentPerGpu[i] = (roundedLoad, roundedTemp);

                    if (i == selIdx)
                    {
                        selectedGpuLoad = roundedLoad;
                        selectedGpuTemp = roundedTemp;
                    }
                }
            }
            else
            {
                SampleSelectedGpuMetrics(null, luidLoads, out float gLoad, out float gTemp);
                selectedGpuLoad = MathF.Round(Math.Clamp(gLoad, 0f, 100f), 1);
                selectedGpuTemp = MathF.Round(Math.Clamp(gTemp, 0f, 120f), 1);
            }

            // 7. Rzeczywista temperatura procesora CPU (°C)
            float sampledCpuTemp = SampleRealCpuTemperature(selectedGpuTemp);

            // 8. Rzeczywiste FPS dla aktywnej gry 3D (0 FPS gdy użytkownik jest na pulpicie / poza grą)
            float sampledFps = SampleRealFps(pid3DLoads, selectedGpuLoad);

            lock (_syncRoot)
            {
                _perGpuMetrics.Clear();
                foreach (var kvp in currentPerGpu)
                {
                    _perGpuMetrics[kvp.Key] = kvp.Value;
                }
                _cpuUsagePercent = MathF.Round(Math.Clamp(sampledCpu, 0f, 100f), 1);
                _ramUsageMb = MathF.Round(Math.Max(0f, sampledRamMb), 0);
                _ramTotalMb = MathF.Round(Math.Max(0f, sampledRamTotal), 0);
                _ramUsagePercent = MathF.Round(Math.Clamp(sampledRamPercent, 0f, 100f), 1);
                _networkDownloadBytesPerSec = Math.Max(0.0, sampledBytesDown);
                _networkUploadBytesPerSec = Math.Max(0.0, sampledBytesUp);
                _networkDownloadMbps = MathF.Round(Math.Max(0f, sampledDown), 1);
                _networkUploadMbps = MathF.Round(Math.Max(0f, sampledUp), 1);
                _diskUsagePercent = MathF.Round(Math.Clamp(sampledDiskPercent, 0f, 100f), 1);
                _diskReadMbps = MathF.Round(sampledDiskReadMb, 1);
                _diskWriteMbps = MathF.Round(sampledDiskWriteMb, 1);
                _gpuUsagePercent = selectedGpuLoad;
                _gpuTemperatureCelsius = selectedGpuTemp;
                _cpuTemperatureCelsius = MathF.Round(Math.Clamp(sampledCpuTemp, 0f, 120f), 1);
                _currentFps = MathF.Round(Math.Max(0f, sampledFps), 0);
            }

            MetricsUpdated?.Invoke();
        }
        catch { }
    }

    private float SampleCpuUsage(float pdhCpuUtility)
    {
        // Ścieżka 1: Natywne Win32 GetSystemTimes (dokładne wyliczenie czasu pracy rdzeni)
        float sysTimesCpu = -1f;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong curIdle = idle.ToULong();
            ulong curKernel = kernel.ToULong();
            ulong curUser = user.ToULong();
            DateTime now = DateTime.UtcNow;

            if (_hasPreviousCpuSample && (now - _lastCpuSampleTime).TotalMilliseconds >= 100)
            {
                ulong idleDelta = curIdle - _lastIdle;
                ulong kernelDelta = curKernel - _lastKernel;
                ulong userDelta = curUser - _lastUser;
                ulong totalDelta = kernelDelta + userDelta;

                _lastIdle = curIdle;
                _lastKernel = curKernel;
                _lastUser = curUser;
                _lastCpuSampleTime = now;

                if (totalDelta > 0)
                {
                    sysTimesCpu = (float)((totalDelta - idleDelta) * 100.0 / totalDelta);
                }
            }
        }

        // Ścieżka 2: Licznik PDH % Processor Utility (zgodny 1:1 z Menedżerem Zadań Windows 11)
        if (pdhCpuUtility > 0.05f && !float.IsNaN(pdhCpuUtility) && !float.IsInfinity(pdhCpuUtility))
        {
            return Math.Clamp(pdhCpuUtility, 0f, 100f);
        }

        if (sysTimesCpu >= 0f)
        {
            return Math.Clamp(sysTimesCpu, 0f, 100f);
        }

        return _cpuUsagePercent;
    }

    private void SamplePdhCounters(
        out float cpuUtility,
        out float diskReadMb,
        out float diskWriteMb,
        out Dictionary<string, float> luidLoads,
        out Dictionary<uint, float> pid3DLoads)
    {
        cpuUtility = -1f;
        diskReadMb = 0f;
        diskWriteMb = 0f;
        luidLoads = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        pid3DLoads = new Dictionary<uint, float>();

        if (!_pdhAvailable || _pdhQuery == IntPtr.Zero)
            return;

        try
        {
            if (PdhCollectQueryData(_pdhQuery) != 0)
                return;

            if (_pdhCpuUtilityCounter != IntPtr.Zero &&
                PdhGetFormattedCounterValue(_pdhCpuUtilityCounter, PDH_FMT_DOUBLE, out _, out PDH_FMT_COUNTERVALUE cpuVal) == 0)
            {
                cpuUtility = (float)cpuVal.doubleValue;
            }

            if (_pdhDiskReadCounter != IntPtr.Zero &&
                PdhGetFormattedCounterValue(_pdhDiskReadCounter, PDH_FMT_DOUBLE, out _, out PDH_FMT_COUNTERVALUE readVal) == 0)
            {
                diskReadMb = Math.Max(0f, (float)(readVal.doubleValue / (1024.0 * 1024.0)));
            }

            if (_pdhDiskWriteCounter != IntPtr.Zero &&
                PdhGetFormattedCounterValue(_pdhDiskWriteCounter, PDH_FMT_DOUBLE, out _, out PDH_FMT_COUNTERVALUE writeVal) == 0)
            {
                diskWriteMb = Math.Max(0f, (float)(writeVal.doubleValue / (1024.0 * 1024.0)));
            }

            if (_pdhGpu3DCounter == IntPtr.Zero)
                return;

            uint bufSize = 0;
            uint itemCount = 0;
            PdhGetFormattedCounterArray(_pdhGpu3DCounter, PDH_FMT_DOUBLE, ref bufSize, out itemCount, IntPtr.Zero);
            if (bufSize == 0 || itemCount == 0)
                return;

            IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
            try
            {
                if (PdhGetFormattedCounterArray(_pdhGpu3DCounter, PDH_FMT_DOUBLE, ref bufSize, out itemCount, buf) == 0)
                {
                    int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
                    for (int i = 0; i < itemCount; i++)
                    {
                        IntPtr itemPtr = IntPtr.Add(buf, i * itemSize);
                        var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(itemPtr);
                        float val = (float)item.FmtValue.doubleValue;
                        if (val <= 0.01f || float.IsNaN(val) || float.IsInfinity(val))
                            continue;

                        string? name = Marshal.PtrToStringUni(item.szName);
                        if (string.IsNullOrEmpty(name))
                            continue;

                        // Wyodrębnij LUID (np. luid_0x00000000_0x000137C7)
                        int luidIdx = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                        if (luidIdx >= 0 && luidIdx + 26 <= name.Length)
                        {
                            string luidKey = name.Substring(luidIdx + 5, 21); // 0x00000000_0x000137C7
                            luidLoads[luidKey] = luidLoads.TryGetValue(luidKey, out float curLuid) ? curLuid + val : val;
                        }

                        // Wyodrębnij PID (np. pid_14636_...)
                        if (name.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
                        {
                            int nextUnderscore = name.IndexOf('_', 4);
                            if (nextUnderscore > 4 && uint.TryParse(name.AsSpan(4, nextUnderscore - 4), out uint pid))
                            {
                                pid3DLoads[pid] = pid3DLoads.TryGetValue(pid, out float curPid) ? curPid + val : val;
                            }
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        catch { }
    }

    private void SampleSelectedGpuMetrics(GpuDeviceInfo? selectedGpu, Dictionary<string, float> luidLoads, out float gpuLoad, out float gpuTemp)
    {
        gpuLoad = 0f;
        gpuTemp = 0f;

        if (selectedGpu == null)
            return;

        string luidKey = $"0x{selectedGpu.LuidHigh:X8}_0x{selectedGpu.LuidLow:X8}";
        float pdhLoad = luidLoads.TryGetValue(luidKey, out float lLoad) ? Math.Clamp(lLoad, 0f, 100f) : 0f;

        // 1. Karta NVIDIA (VendorId == 0x10DE lub nazwa zawiera NVIDIA/GeForce/RTX/GTX)
        if (selectedGpu.VendorId == 0x10DE ||
            selectedGpu.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            selectedGpu.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            selectedGpu.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase))
        {
            if (_nvmlInitialized && _nvmlDevices.Count > 0)
            {
                IntPtr handle = _nvmlDevices[0].Handle;
                foreach (var dev in _nvmlDevices)
                {
                    if (string.Equals(dev.Name, selectedGpu.Name, StringComparison.OrdinalIgnoreCase) ||
                        selectedGpu.Name.Contains(dev.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        handle = dev.Handle;
                        break;
                    }
                }

                if (nvmlDeviceGetTemperature(handle, 0 /* NVML_TEMPERATURE_GPU */, out uint tempC) == 0)
                {
                    gpuTemp = tempC;
                }

                if (nvmlDeviceGetUtilizationRates(handle, out nvmlUtilization_t util) == 0)
                {
                    gpuLoad = Math.Max(util.gpu, pdhLoad);
                }
                else
                {
                    gpuLoad = pdhLoad;
                }
                return;
            }
        }

        // 2. Karta AMD Radeon (VendorId == 0x1002 lub nazwa zawiera AMD/Radeon)
        if (selectedGpu.VendorId == 0x1002 ||
            selectedGpu.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            selectedGpu.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            if (_adlAvailable && _adlContext != IntPtr.Zero && _adlPmLogBuffer != IntPtr.Zero)
            {
                int adlIndex = 0;
                foreach (var adlAdapter in _adlAdapters)
                {
                    if (string.Equals(adlAdapter.Name, selectedGpu.Name, StringComparison.OrdinalIgnoreCase) ||
                        adlAdapter.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                    {
                        adlIndex = adlAdapter.AdapterIndex;
                        break;
                    }
                }

                if (TryReadAdlPmLog(adlIndex, out float adlGfxLoad, out float adlGfxTemp, out _))
                {
                    gpuLoad = Math.Max(adlGfxLoad, pdhLoad);
                    gpuTemp = adlGfxTemp;
                    return;
                }
            }
        }

        // 3. Fallback (Intel Arc/Iris/UHD lub brak dedykowanego API producenta)
        gpuLoad = pdhLoad;
        if (gpuTemp <= 0f && TryReadLibreHardwareMonitor(out _, out float lhmGpuTemp, out float lhmGpuLoad))
        {
            if (lhmGpuTemp > 0f) gpuTemp = lhmGpuTemp;
            if (lhmGpuLoad >= 0f && gpuLoad <= 0f) gpuLoad = lhmGpuLoad;
        }
    }

    private bool TryReadAdlPmLog(int adapterIndex, out float gfxActivityPercent, out float gpuTempC, out float cpuOrSocTempC)
    {
        gfxActivityPercent = 0f;
        gpuTempC = 0f;
        cpuOrSocTempC = 0f;

        if (!_adlAvailable || _adlContext == IntPtr.Zero || _adlPmLogBuffer == IntPtr.Zero)
            return false;

        try
        {
            for (int k = 0; k < 516; k++) Marshal.WriteInt32(_adlPmLogBuffer, k * 4, 0);

            if (ADL2_New_QueryPMLogData_Get(_adlContext, adapterIndex, _adlPmLogBuffer) != 0)
                return false;

            int ReadSensor(int sensorId, out bool supported)
            {
                supported = Marshal.ReadInt32(_adlPmLogBuffer, 4 + sensorId * 8) != 0;
                return Marshal.ReadInt32(_adlPmLogBuffer, 4 + sensorId * 8 + 4);
            }

            // Obciążenie rdzenia graficznego AMD (ADL_PMLOG_INFO_ACTIVITY_GFX = 19)
            int actGfx = ReadSensor(19, out bool supAct);
            if (supAct && actGfx >= 0 && actGfx <= 100)
            {
                gfxActivityPercent = actGfx;
            }

            // Temperatura GPU AMD:
            // - ADL_PMLOG_TEMPERATURE_EDGE (8) - dla dedykowanych kart Radeon
            // - ADL_PMLOG_TEMPERATURE_GFX (28) - dla zintegrowanych i nowoczesnych układów RDNA
            // - ADL_PMLOG_TEMPERATURE_HOTSPOT (27)
            int tempEdge = ReadSensor(8, out bool supEdge);
            int tempGfx = ReadSensor(28, out bool supGfx);
            int tempHotspot = ReadSensor(27, out bool supHotspot);
            int tempSoc = ReadSensor(29, out bool supSoc);
            int tempCpu = ReadSensor(32, out bool supCpu);

            if (supEdge && tempEdge > 0 && tempEdge < 125)
                gpuTempC = tempEdge;
            else if (supGfx && tempGfx > 0 && tempGfx < 125)
                gpuTempC = tempGfx;
            else if (supHotspot && tempHotspot > 0 && tempHotspot < 125)
                gpuTempC = tempHotspot;
            else if (supSoc && tempSoc > 0 && tempSoc < 125)
                gpuTempC = tempSoc;

            // Temperatura CPU / SoC (dla procesorów AMD Ryzen z układem IOD/Radeon)
            if (supCpu && tempCpu > 15 && tempCpu < 120)
                cpuOrSocTempC = tempCpu;
            else if (supSoc && tempSoc > 15 && tempSoc < 120)
                cpuOrSocTempC = tempSoc;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static readonly HttpClient s_lhmClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(500)
    };
    private static DateTime s_lastLhmCheck = DateTime.MinValue;
    private static DateTime s_lastLhmProcessCheck = DateTime.MinValue;
    private static bool s_cachedLhmProcessRunning = false;
    private static float s_cachedLhmCpuTemp = 0f;
    private static float s_cachedLhmGpuTemp = 0f;
    private static float s_cachedLhmGpuLoad = -1f;

    private static bool IsLhmProcessRunning()
    {
        if (DateTime.UtcNow - s_lastLhmProcessCheck < TimeSpan.FromSeconds(5))
        {
            return s_cachedLhmProcessRunning;
        }
        s_lastLhmProcessCheck = DateTime.UtcNow;
        try
        {
            s_cachedLhmProcessRunning = Process.GetProcessesByName("LibreHardwareMonitor").Length > 0;
        }
        catch
        {
            s_cachedLhmProcessRunning = false;
        }
        return s_cachedLhmProcessRunning;
    }

    private static bool TryReadLibreHardwareMonitor(out float cpuTemp, out float gpuTemp, out float gpuLoad)
    {
        cpuTemp = 0f;
        gpuTemp = 0f;
        gpuLoad = -1f;

        if (DateTime.UtcNow < s_lastLhmCheck)
        {
            if (s_cachedLhmCpuTemp > 0 || s_cachedLhmGpuTemp > 0)
            {
                cpuTemp = s_cachedLhmCpuTemp;
                gpuTemp = s_cachedLhmGpuTemp;
                gpuLoad = s_cachedLhmGpuLoad;
                return true;
            }
            return false;
        }

        if (!IsLhmProcessRunning())
        {
            s_lastLhmCheck = DateTime.UtcNow.AddSeconds(10);
            return false;
        }

        try
        {
            using var resp = s_lhmClient.GetAsync("http://127.0.0.1:8085/data.json").GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                s_lastLhmCheck = DateTime.UtcNow.AddSeconds(30);
                return false;
            }

            string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);

            float foundCpuTemp = 0f;
            float foundGpuTemp = 0f;
            float foundGpuLoad = -1f;

            void WalkNode(JsonElement node)
            {
                if (node.TryGetProperty("SensorId", out var idElem) && node.TryGetProperty("Type", out var typeElem))
                {
                    string id = idElem.GetString() ?? "";
                    string type = typeElem.GetString() ?? "";
                    string text = node.TryGetProperty("Text", out var textElem) ? (textElem.GetString() ?? "") : "";

                    float val = 0f;
                    bool hasVal = false;
                    if (node.TryGetProperty("RawValue", out var rawElem) && rawElem.TryGetSingle(out float rawFloat))
                    {
                        val = rawFloat;
                        hasVal = true;
                    }
                    else if (node.TryGetProperty("Value", out var valElem))
                    {
                        string vStr = valElem.GetString() ?? "";
                        string part = vStr.Split(' ')[0].Replace(',', '.');
                        if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                        {
                            val = parsed;
                            hasVal = true;
                        }
                    }

                    if (hasVal)
                    {
                        if (type == "Temperature")
                        {
                            if (id.Contains("cpu/") || text.Contains("Core (Tctl", StringComparison.OrdinalIgnoreCase) || text.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            {
                                if (val > foundCpuTemp && val < 120f) foundCpuTemp = val;
                            }
                            else if (id.StartsWith("/gpu") || text.Contains("GPU Core", StringComparison.OrdinalIgnoreCase))
                            {
                                if (val > foundGpuTemp && val < 120f) foundGpuTemp = val;
                            }
                        }
                        else if (type == "Load" && (id.StartsWith("/gpu") || text.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) || text.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (val > foundGpuLoad && val <= 100f) foundGpuLoad = val;
                        }
                    }
                }

                if (node.TryGetProperty("Children", out var childrenElem) && childrenElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in childrenElem.EnumerateArray())
                    {
                        WalkNode(child);
                    }
                }
            }

            WalkNode(doc.RootElement);

            if (foundCpuTemp > 0 || foundGpuTemp > 0)
            {
                s_cachedLhmCpuTemp = foundCpuTemp;
                s_cachedLhmGpuTemp = foundGpuTemp;
                s_cachedLhmGpuLoad = foundGpuLoad;

                cpuTemp = foundCpuTemp;
                gpuTemp = foundGpuTemp;
                gpuLoad = foundGpuLoad;
                s_lastLhmCheck = DateTime.UtcNow.AddMilliseconds(1500);
                return true;
            }
        }
        catch { }

        s_lastLhmCheck = DateTime.UtcNow.AddSeconds(30);
        return false;
    }

    private float SampleRealCpuTemperature(float currentGpuTemp)
    {
        // 1. Sprawdź pamięć współdzieloną CoreTemp (CoreTempMappingObject)
        if (TryReadCoreTempSharedMemory(out float coreTemp) && coreTemp > 15f && coreTemp < 120f)
        {
            return coreTemp;
        }

        // 2. Sprawdź czujniki LibreHardwareMonitor (LHM web server na localhost:8085)
        if (TryReadLibreHardwareMonitor(out float lhmCpu, out _, out _) && lhmCpu > 15f && lhmCpu < 120f)
        {
            return lhmCpu;
        }

        // 2. Sprawdź AMD ADL2 PMLog (dla procesorów AMD Ryzen 7000/9000/APU raportujących temperaturę CPU/SoC)
        if (_adlAvailable && _adlContext != IntPtr.Zero)
        {
            int amdAdapterIdx = 0;
            foreach (var adl in _adlAdapters)
            {
                if (adl.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                    adl.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    amdAdapterIdx = adl.AdapterIndex;
                    break;
                }
            }

            if (TryReadAdlPmLog(amdAdapterIdx, out _, out _, out float ryzenTemp) && ryzenTemp > 15f && ryzenTemp < 120f)
            {
                return ryzenTemp;
            }
        }

        // 3. Jeśli żaden czujnik CPU nie jest dostępny bez sterownika Ring-0, zwróć ostatnią znaną wartość lub estymację
        if (_cpuTemperatureCelsius > 15f && _cpuTemperatureCelsius < 120f)
        {
            return _cpuTemperatureCelsius;
        }

        if (currentGpuTemp > 25f && currentGpuTemp < 115f)
        {
            return Math.Clamp(currentGpuTemp - 2f, 35f, 95f);
        }

        return Math.Clamp(38f + (_cpuUsagePercent * 0.35f), 35f, 85f);
    }

    private static bool TryReadCoreTempSharedMemory(out float cpuTemp)
    {
        cpuTemp = 0f;
        IntPtr hMap = OpenFileMappingW(FILE_MAP_READ, false, "CoreTempMappingObject");
        if (hMap == IntPtr.Zero)
            return false;

        try
        {
            IntPtr pView = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, (UIntPtr)1600);
            if (pView == IntPtr.Zero)
                return false;

            try
            {
                uint coreCount = (uint)Marshal.ReadInt32(pView, 1540);
                if (coreCount > 0 && coreCount <= 64)
                {
                    float maxTemp = 0f;
                    int[] rawFloats = new int[coreCount];
                    Marshal.Copy(IntPtr.Add(pView, 4), rawFloats, 0, (int)coreCount);
                    for (int i = 0; i < coreCount; i++)
                    {
                        float t = BitConverter.Int32BitsToSingle(rawFloats[i]);
                        if (t > maxTemp && t < 125f) maxTemp = t;
                    }
                    if (maxTemp > 15f)
                    {
                        cpuTemp = maxTemp;
                        return true;
                    }
                }
            }
            finally
            {
                UnmapViewOfFile(pView);
            }
        }
        catch { }
        finally
        {
            CloseHandle(hMap);
        }
        return false;
    }

    private float SampleRealFps(Dictionary<uint, float> pid3DLoads, float currentGpuLoad)
    {
        // 1. Sprawdź jakie okno jest aktualnie na pierwszym planie (Foreground Window)
        IntPtr fgHwnd = GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero)
        {
            _smoothedFps = 0f;
            return 0f;
        }

        GetWindowThreadProcessId(fgHwnd, out uint fgPid);
        if (fgPid == 0)
        {
            _smoothedFps = 0f;
            return 0f;
        }

        // 2. Sprawdź, czy aktywny proces pierwszoplanowy to pulpit / przeglądarka / aplikacja systemowa / MasterHub
        string procName = string.Empty;
        try
        {
            using var fgProc = Process.GetProcessById((int)fgPid);
            procName = fgProc.ProcessName;
        }
        catch
        {
            _smoothedFps = 0f;
            return 0f;
        }

        if (string.IsNullOrEmpty(procName) ||
            s_nonGameProcesses.Contains(procName) ||
            string.Equals(procName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            // Na pulpicie, w przeglądarce lub w samej aplikacji MasterHub FPS wynosi 0
            _smoothedFps = 0f;
            return 0f;
        }

        // 3. Poziom 1: Jeśli aktywny jest RivaTuner Statistics Server (RTSS), odczytaj dokładny zmierzony FPS procesu
        if (TryReadRtssFps(fgPid, out float rtssFps) && rtssFps > 0.5f)
        {
            _smoothedFps = rtssFps;
            return rtssFps;
        }

        // 4. Poziom 2: NVIDIA FrameView / PresentMon Shared Memory (Global\FVSDKSharedMemory)
        // Rzeczywisty pomiar klatek z telemetrii sprzętowej DirectX / Vulkan sterownika graficznego
        if (TryReadFvSdkFps(procName, out float fvFps) && fvFps > 0.5f)
        {
            _smoothedFps = fvFps;
            return fvFps;
        }

        // 5. Poziom 3: Sprawdź licznik klatek sterownika AMD ADL2 (ADL2_Adapter_FrameMetrics_Get) jeśli dostępny
        if (_adlAvailable && _adlContext != IntPtr.Zero)
        {
            try
            {
                if (!_adlFrameMetricsStarted)
                {
                    _adlFrameMetricsStarted = ADL2_Adapter_FrameMetrics_Start(_adlContext, 0, 0) == 0;
                }

                if (_adlFrameMetricsStarted &&
                    ADL2_Adapter_FrameMetrics_Get(_adlContext, 0, 0, out float adlFps) == 0 &&
                    adlFps > 0.5f && adlFps <= 1000f)
                {
                    _smoothedFps = adlFps;
                    return adlFps;
                }
            }
            catch { }
        }

        // 6. Poziom 4: Pulpit / brak aktywnej gry 3D - odczytaj fizyczną częstotliwość odświeżania głównego monitora
        try
        {
            var devMode = new DEVMODEA { dmSize = (short)Marshal.SizeOf<DEVMODEA>() };
            if (EnumDisplaySettingsA(null, -1, ref devMode) && devMode.dmDisplayFrequency > 0)
            {
                _smoothedFps = devMode.dmDisplayFrequency;
                return devMode.dmDisplayFrequency;
            }
        }
        catch { }

        _smoothedFps = 0f;
        return 0f;
    }

    private static bool TryReadFvSdkFps(string targetProcessName, out float fps)
    {
        fps = 0f;
        IntPtr hMap = OpenFileMappingW(FILE_MAP_READ, false, "Global\\FVSDKSharedMemory");
        if (hMap == IntPtr.Zero)
            return false;

        try
        {
            IntPtr pView = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, (UIntPtr)98304);
            if (pView == IntPtr.Zero)
                return false;

            try
            {
                byte[] buf = new byte[98304];
                Marshal.Copy(pView, buf, 0, 98304);

                float bestExactFps = 0f;
                float bestAnyGameFps = 0f;

                // Sprawdź Buffer A (offset bazowy 0x0000) oraz Buffer B (offset bazowy 0xA480).
                // Uszereguj bufory tak, aby bufor o nowszym znaczniku czasu (front buffer) był sprawdzany jako pierwszy.
                long tsA = BitConverter.ToInt64(buf, 0);
                long tsB = BitConverter.ToInt64(buf, 0xA480);
                int[] bufferOffsets = (tsB > tsA) ? new[] { 0xA480, 0 } : new[] { 0, 0xA480 };
                const int stride = 2632;

                foreach (int baseOff in bufferOffsets)
                {
                    for (int slot = 0; slot < 16; slot++)
                    {
                        int exeOffset = baseOff + 0x0BA0 + slot * stride;
                        if (exeOffset + 64 >= baseOff + 0xA480) break;

                        // Odczytaj nazwę procesu w slocie
                        byte[] nameBytes = new byte[64];
                        Array.Copy(buf, exeOffset, nameBytes, 0, 64);
                        int nullIdx = Array.IndexOf(nameBytes, (byte)0);
                        string name = nullIdx >= 0 ? Encoding.ASCII.GetString(nameBytes, 0, nullIdx) : Encoding.ASCII.GetString(nameBytes);
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        string nameClean = Path.GetFileNameWithoutExtension(name.Trim());
                        if (s_nonGameProcesses.Contains(name) || s_nonGameProcesses.Contains(nameClean))
                            continue;

                        // Prawdziwy renderowany FPS gry z NVIDIA FrameView SDK / PresentMon:
                        // +0x04D0: AvgRenderFPS (średni FPS renderowania klatek silnika gry, okno 1s - poprawny wskaźnik!)
                        // +0x02B0: CurRenderFPS (chwilowy FPS renderowania klatki: 1000.0 / msBetweenPresents)
                        // +0x04D8: AvgDispFPS (średni FPS wyświetlania na ekranie)
                        // Uwaga: dawny offset +0x0438 wskazywał na metrykę FPS_Low_01 (1% Low FPS), co zaniżało wynik podczas mikroprzycięć!
                        double avgRender = BitConverter.ToDouble(buf, baseOff + 0x04D0 + slot * stride);
                        double curRender = BitConverter.ToDouble(buf, baseOff + 0x02B0 + slot * stride);
                        double avgDisp   = BitConverter.ToDouble(buf, baseOff + 0x04D8 + slot * stride);

                        double chosenFps = 0.0;
                        if (!double.IsNaN(avgRender) && avgRender > 0.5 && avgRender <= 2000.0)
                        {
                            if (!double.IsNaN(curRender) && curRender > 0.5 && Math.Abs(curRender - avgRender) < 3.0)
                            {
                                chosenFps = Math.Max(avgRender, curRender);
                            }
                            else
                            {
                                chosenFps = avgRender;
                            }
                        }
                        else if (!double.IsNaN(curRender) && curRender > 0.5 && curRender <= 2000.0)
                        {
                            chosenFps = curRender;
                        }
                        else if (!double.IsNaN(avgDisp) && avgDisp > 0.5 && avgDisp <= 2000.0)
                        {
                            chosenFps = avgDisp;
                        }

                        if (chosenFps <= 0.5)
                            continue;

                        float slotFps = (float)chosenFps;

                        bool isMatch = !string.IsNullOrEmpty(targetProcessName) &&
                            (string.Equals(targetProcessName, name, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(targetProcessName, nameClean, StringComparison.OrdinalIgnoreCase) ||
                             name.Contains(targetProcessName, StringComparison.OrdinalIgnoreCase) ||
                             targetProcessName.Contains(nameClean, StringComparison.OrdinalIgnoreCase));

                        if (isMatch)
                        {
                            bestExactFps = slotFps;
                            break;
                        }
                        else if (slotFps > bestAnyGameFps)
                        {
                            bestAnyGameFps = slotFps;
                        }
                    }

                    if (bestExactFps > 0f)
                    {
                        break;
                    }
                }

                if (bestExactFps > 0f)
                {
                    fps = bestExactFps;
                    return true;
                }

                if (bestAnyGameFps > 0f)
                {
                    fps = bestAnyGameFps;
                    return true;
                }
            }
            finally
            {
                UnmapViewOfFile(pView);
            }
        }
        catch { }
        finally
        {
            CloseHandle(hMap);
        }

        return false;
    }

    private static bool TryReadRtssFps(uint targetPid, out float fps)
    {
        fps = 0f;
        IntPtr hMap = OpenFileMappingW(FILE_MAP_READ, false, "RTSSSharedMemoryV2");
        if (hMap == IntPtr.Zero)
            return false;

        try
        {
            IntPtr pHeader = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, (UIntPtr)36);
            if (pHeader == IntPtr.Zero)
                return false;

            uint signature;
            uint appEntrySize;
            uint appArrOffset;
            uint appArrSize;
            try
            {
                signature = (uint)Marshal.ReadInt32(pHeader, 0);
                appEntrySize = (uint)Marshal.ReadInt32(pHeader, 8);
                appArrOffset = (uint)Marshal.ReadInt32(pHeader, 12);
                appArrSize = (uint)Marshal.ReadInt32(pHeader, 16);
            }
            finally
            {
                UnmapViewOfFile(pHeader);
            }

            // 'RTSS' == 0x52545353
            if (signature != 0x52545353u || appEntrySize < 280 || appArrSize == 0 || appArrSize > 256)
                return false;

            ulong totalBytes = appArrOffset + (ulong)appEntrySize * appArrSize;
            IntPtr pView = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, (UIntPtr)totalBytes);
            if (pView == IntPtr.Zero)
                return false;

            try
            {
                float bestFps = 0f;

                for (uint i = 0; i < appArrSize; i++)
                {
                    int entryOffset = (int)(appArrOffset + i * appEntrySize);
                    uint procId = (uint)Marshal.ReadInt32(pView, entryOffset);
                    if (procId == 0)
                        continue;

                    // Odczytaj nazwę procesu z bufora szName (offset 4, max 260 znaków)
                    byte[] nameBytes = new byte[260];
                    Marshal.Copy(IntPtr.Add(pView, entryOffset + 4), nameBytes, 0, 260);
                    int zeroIdx = Array.IndexOf(nameBytes, (byte)0);
                    string entryName = zeroIdx >= 0 ? Encoding.ASCII.GetString(nameBytes, 0, zeroIdx) : Encoding.ASCII.GetString(nameBytes);
                    string entryNameNoExt = Path.GetFileNameWithoutExtension(entryName);

                    if (s_nonGameProcesses.Contains(entryName) || s_nonGameProcesses.Contains(entryNameNoExt))
                        continue;

                    // Prawidłowe offsety struktur RTSS_SHARED_MEMORY_APP_ENTRY:
                    // +264: dwFlags, +268: dwTime0, +272: dwTime1, +276: dwFrames, +288: dwFramerate
                    uint time0 = (uint)Marshal.ReadInt32(pView, entryOffset + 268);
                    uint time1 = (uint)Marshal.ReadInt32(pView, entryOffset + 272);
                    uint frames = (uint)Marshal.ReadInt32(pView, entryOffset + 276);

                    float entryFps = 0f;

                    // 1. Priorytet: Odczytaj bezpośrednio skalibrowany, wygładzony framerate wyliczony przez sam silnik RTSS (QPC)
                    if (appEntrySize >= 292)
                    {
                        uint rawFramerate = (uint)Marshal.ReadInt32(pView, entryOffset + 288);
                        if (rawFramerate > 0)
                        {
                            entryFps = (rawFramerate >= 10000) ? (rawFramerate / 1000f) : (rawFramerate / 10f);
                        }
                    }

                    // 2. Fallback: Jeśli dwFramerate nie jest wypełniony, wylicz na podstawie liczby klatek i czasu
                    if (entryFps <= 0f && time1 > time0 && frames > 0)
                    {
                        entryFps = frames * 1000f / (time1 - time0);
                    }

                    if (entryFps > 0f && entryFps <= 1000f)
                    {
                        if (procId == targetPid)
                        {
                            fps = entryFps;
                            return true;
                        }

                        if (entryFps > bestFps)
                        {
                            bestFps = entryFps;
                        }
                    }
                }

                if (bestFps > 0f)
                {
                    fps = bestFps;
                    return true;
                }
            }
            finally
            {
                UnmapViewOfFile(pView);
            }
        }
        catch { }
        finally
        {
            CloseHandle(hMap);
        }
        return false;
    }

    private static bool IsIgnoredNetworkInterface(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up ||
            nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
            nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
        {
            return true;
        }

        string name = nic.Name;
        string desc = nic.Description;

        return name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Pseudo-Interface", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SampleNetworkCounters(out long totalReceived, out long totalSent)
    {
        totalReceived = 0;
        totalSent = 0;
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            bool foundPhysical = false;

            // Najpierw zlicz adaptery fizyczne (odrzucając wirtualne adaptery maszyn wirtualnych i tuneli)
            foreach (var nic in interfaces)
            {
                if (IsIgnoredNetworkInterface(nic)) continue;

                var stats = nic.GetIPStatistics();
                totalReceived += stats.BytesReceived;
                totalSent += stats.BytesSent;
                foundPhysical = true;
            }

            // Jeśli nie znaleziono żadnej fizycznej karty (np. czyste środowisko zwirtualizowane), zlicz dowolną aktywną kartę non-loopback
            if (!foundPhysical)
            {
                foreach (var nic in interfaces)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var stats = nic.GetIPStatistics();
                    totalReceived += stats.BytesReceived;
                    totalSent += stats.BytesSent;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _timer?.Dispose();
                _timer = null;
            }

            if (_pdhQuery != IntPtr.Zero)
            {
                try { PdhCloseQuery(_pdhQuery); } catch { }
                _pdhQuery = IntPtr.Zero;
            }

            if (_adlContext != IntPtr.Zero)
            {
                try
                {
                    if (_adlFrameMetricsStarted)
                    {
                        ADL2_Adapter_FrameMetrics_Stop(_adlContext, 0, 0);
                        _adlFrameMetricsStarted = false;
                    }
                    ADL2_Main_Control_Destroy(_adlContext);
                }
                catch { }
                _adlContext = IntPtr.Zero;
            }

            if (_adlPmLogBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_adlPmLogBuffer);
                _adlPmLogBuffer = IntPtr.Zero;
            }

            if (_nvmlInitialized)
            {
                try { nvmlShutdown(); } catch { }
                _nvmlInitialized = false;
            }

            _disposed = true;
        }
    }

    #region Native Interop (Win32, DXGI, NVML, AMD ADL2, PDH)

    private const uint FILE_MAP_READ = 0x0004;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern IntPtr OpenFileMappingW(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("kernel32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint DateTimeLow;
        public uint DateTimeHigh;

        public ulong ToULong() => ((ulong)DateTimeHigh << 32) | DateTimeLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsA(string? lpszDeviceName, int iModeNum, ref DEVMODEA lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODEA
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    // --- DWM Composition Timing Interop (dwmapi.dll) ---
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct UNSIGNED_RATIO
    {
        public uint uiNumerator;
        public uint uiDenominator;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DWM_TIMING_INFO
    {
        public uint cbSize;
        public UNSIGNED_RATIO rateRefresh;
        public ulong qpcRefreshPeriod;
        public UNSIGNED_RATIO rateCompose;
        public ulong qpcVBlank;
        public ulong cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose;
        public ulong cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame;
        public ulong cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong framesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed;
        public ulong qpcFrameDisplayed;
        public ulong cRefreshFrameDisplayed;
        public ulong cFrameComplete;
        public ulong qpcFrameComplete;
        public ulong cFramePending;
        public ulong qpcFramePending;
        public ulong cFramesDisplayed;
        public ulong cFramesComplete;
        public ulong cFramesPending;
        public ulong cFramesAvailable;
        public ulong cFramesDropped;
        public ulong cFramesMissed;
        public ulong cRefreshNextDisplayed;
        public ulong cRefreshNextPresented;
        public ulong cRefreshesDisplayed;
        public ulong cRefreshesPresented;
        public ulong cRefreshStarted;
        public ulong cPixelsReceived;
        public ulong cPixelsDrawn;
        public ulong cBuffersEmpty;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DWM_TIMING_INFO info);

    // --- DXGI Interop ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();
        [PreserveSig]
        int EnumAdapters1(uint Adapter, out IDXGIAdapter1? ppAdapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();
        [PreserveSig]
        int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1? ppFactory);

    // --- NVIDIA NVML Interop (nvml.dll) ---
    [StructLayout(LayoutKind.Sequential)]
    private struct nvmlUtilization_t
    {
        public uint gpu;
        public uint memory;
    }

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlInit_v2();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlShutdown();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetCount_v2(out uint deviceCount);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization_t utilization);

    // --- AMD ADL2 Interop (atiadlxx.dll) ---
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ADL_Main_Memory_AllocDelegate(int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ADLAdapterInfo
    {
        public int iSize;
        public int iAdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strUDID;
        public int iBusNumber;
        public int iDeviceNumber;
        public int iFunctionNumber;
        public int iVendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strDisplayName;
        public int iPresent;
        public int iExist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strDriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strDriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strPNPString;
        public int iOSDisplayIndex;
    }

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Main_Control_Create(ADL_Main_Memory_AllocDelegate callback, int iEnumConnectedAdapters, out IntPtr context);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Main_Control_Destroy(IntPtr context);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int lpNumAdapters);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr lpInfo, int iInputSize);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int iAdapterIndex, IntPtr lpDataOutput);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_FrameMetrics_Start(IntPtr context, int iAdapterIndex, int VidPnSourceId);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_FrameMetrics_Stop(IntPtr context, int iAdapterIndex, int VidPnSourceId);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_FrameMetrics_Get(IntPtr context, int iAdapterIndex, int VidPnSourceId, out float fFPS);

    // --- Win32 PDH Interop (pdh.dll) ---
    private const uint PDH_FMT_DOUBLE = 0x00000200;

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE
    {
        public uint CStatus;
        public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, out uint lpdwItemCount, IntPtr ItemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr hQuery);

    #endregion
}

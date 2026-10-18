using System;
using System.Collections.Generic;

namespace MasterHub.Core.Monitoring;

/// <summary>
/// Reprezentuje wykrytą w systemie sprzętową kartę graficzną (GPU).
/// </summary>
public sealed record GpuDeviceInfo(
    int Index,
    string Name,
    uint VendorId,
    ulong DedicatedVramMb,
    uint LuidLow,
    int LuidHigh)
{
    /// <summary>Czytelna etykieta wyświetlana na liście wyboru GPU w UI.</summary>
    public string DisplayLabel => DedicatedVramMb >= 1024
        ? $"{Name} ({DedicatedVramMb / 1024.0:0.#} GB)"
        : (DedicatedVramMb > 0 ? $"{Name} ({DedicatedVramMb} MB)" : Name);

    public override string ToString() => DisplayLabel;
}

/// <summary>
/// Interfejs serwisu monitorującego zasoby systemowe w czasie rzeczywistym (Etap 3).
/// Dostarcza rzeczywiste metryki sprzętowe CPU, RAM, GPU (z wyborem karty z listy), temperaturę, prędkość sieci, dysku oraz FPS.
/// </summary>
public interface ISystemMonitorService : IDisposable
{
    /// <summary>Czy serwis aktywnie monitoruje parametry systemowe.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Interwał próbkowania metryk w milisekundach (domyślnie 1000 ms).</summary>
    int RefreshIntervalMs { get; set; }

    /// <summary>Lista wykrytych sprzętowych kart graficznych (GPU) w systemie.</summary>
    IReadOnlyList<GpuDeviceInfo> AvailableGpus { get; }

    /// <summary>Lista czytelnych nazw kart graficznych do bindowania w UI.</summary>
    IReadOnlyList<string> AvailableGpuNames { get; }

    /// <summary>Indeks aktualnie wybranej karty graficznej z listy <see cref="AvailableGpus"/>.</summary>
    int SelectedGpuIndex { get; set; }

    /// <summary>Nazwa aktualnie monitorowanej karty graficznej GPU.</summary>
    string SelectedGpuName { get; }

    /// <summary>Obciążenie procesora CPU (0 - 100%).</summary>
    float CpuUsagePercent { get; }

    /// <summary>Wykorzystanie pamięci RAM w megabajtach (MB).</summary>
    float RamUsageMb { get; }

    /// <summary>Całkowita pamięć fizyczna RAM w megabajtach (MB).</summary>
    float RamTotalMb { get; }

    /// <summary>Procentowe wykorzystanie pamięci RAM (0 - 100%).</summary>
    float RamUsagePercent { get; }

    /// <summary>Rzeczywiste obciążenie wybranej karty graficznej GPU (0 - 100%).</summary>
    float GpuUsagePercent { get; }

    /// <summary>Rzeczywista temperatura procesora CPU w stopniach Celsjusza (°C).</summary>
    float CpuTemperatureCelsius { get; }

    /// <summary>Rzeczywista temperatura wybranej karty graficznej GPU w stopniach Celsjusza (°C).</summary>
    float GpuTemperatureCelsius { get; }

    /// <summary>Zwraca obciążenie GPU dla wskazanego indeksu karty graficznej (0 - 100%).</summary>
    float GetGpuUsagePercent(int gpuIndex);

    /// <summary>Zwraca temperaturę GPU dla wskazanego indeksu karty graficznej w stopniach Celsjusza (°C).</summary>
    float GetGpuTemperatureCelsius(int gpuIndex);

    /// <summary>Prędkość pobierania sieci w megabitach na sekundę (Mb/s).</summary>
    float NetworkDownloadMbps { get; }

    /// <summary>Prędkość wysyłania sieci w megabitach na sekundę (Mb/s).</summary>
    float NetworkUploadMbps { get; }

    /// <summary>Rzeczywista prędkość pobierania sieci w bajtach na sekundę (B/s).</summary>
    double NetworkDownloadBytesPerSec { get; }

    /// <summary>Rzeczywista prędkość wysyłania sieci w bajtach na sekundę (B/s).</summary>
    double NetworkUploadBytesPerSec { get; }

    /// <summary>Zajętość dysku systemowego w procentach (0 - 100%).</summary>
    float DiskUsagePercent { get; }

    /// <summary>Lista dostępnych gotowych dysków i partycji w systemie.</summary>
    IReadOnlyList<string> AvailableDrives { get; }

    /// <summary>Zwraca zajętość wskazanego dysku w procentach (0 - 100%). Jeśli parametr jest pusty, zwraca dysk systemowy.</summary>
    float GetDiskUsagePercent(string? driveName = null);

    /// <summary>Rzeczywista prędkość odczytu z dysku w megabajtach na sekundę (MB/s).</summary>
    float DiskReadMbps { get; }

    /// <summary>Rzeczywista prędkość zapisu na dysku w megabajtach na sekundę (MB/s).</summary>
    float DiskWriteMbps { get; }

    /// <summary>Rzeczywista liczba klatek na sekundę (FPS) dla aktywnej gry 3D (0 na pulpicie).</summary>
    float CurrentFps { get; }

    /// <summary>Zdarzenie wywoływane po zaktualizowaniu metryk systemowych.</summary>
    event Action? MetricsUpdated;

    /// <summary>Odświeża listę dostępnych w systemie kart graficznych GPU.</summary>
    void RefreshGpuList();

    /// <summary>Wymusza natychmiastowe odświeżenie metryk.</summary>
    void RefreshNow();
}

using System.Diagnostics;
using MasterHub.Core.Monitoring;

namespace MasterHub.Core.Tests;

public class MonitorDemandTests
{
    private static readonly System.Reflection.MethodInfo SampleMethod =
        typeof(SystemMonitorService).GetMethod("SampleMetricsInternal",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private static double AverageTickMs(SystemMonitorService mon, MonitorMetrics scope, int n = 60)
    {
        mon.RequiredMetrics = scope;
        SampleMethod.Invoke(mon, null);   // rozgrzewka
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) SampleMethod.Invoke(mon, null);
        return sw.Elapsed.TotalMilliseconds / n;
    }

    [Fact]
    public void DefaultIsAllMetrics_SoExistingConsumersKeepWorking()
    {
        using var mon = new SystemMonitorService();
        Assert.Equal(MonitorMetrics.All, mon.RequiredMetrics);
    }

    [Fact]
    public void RequestedMetric_IsSampled_AndNothingElse()
    {
        using var mon = new SystemMonitorService();
        mon.IsEnabled = false;   // zatrzymaj timer - tiki wywołujemy ręcznie

        // konstruktor robi jedno próbkowanie bazowe, więc porównujemy z wartościami sprzed ticka (a nie z zerem)
        float netBefore = mon.NetworkDownloadMbps, fpsBefore = mon.CurrentFps, diskBefore = mon.DiskReadMbps;

        mon.RequiredMetrics = MonitorMetrics.Ram;
        SampleMethod.Invoke(mon, null);

        Assert.InRange(mon.RamUsagePercent, 0.1f, 100f);
        Assert.True(mon.RamTotalMb > 0);
        Assert.Equal(netBefore, mon.NetworkDownloadMbps);   // sieć nie była proszona - wartość bez zmian
        Assert.Equal(fpsBefore, mon.CurrentFps);
        Assert.Equal(diskBefore, mon.DiskReadMbps);
    }

    [Fact]
    public void NoRequestedMetrics_TickIsFarCheaperThanFullSampling()
    {
        using var mon = new SystemMonitorService();
        mon.IsEnabled = false;

        double all = AverageTickMs(mon, MonitorMetrics.All);
        double none = AverageTickMs(mon, MonitorMetrics.None);

        Console.WriteLine($"[PERF] monitor tick: All={all:F3} ms, None={none:F4} ms");
        Assert.True(none * 5 < all, $"tik bez metryk ({none:F4} ms) powinien być wielokrotnie tańszy niż pełne próbkowanie ({all:F3} ms)");
    }
}

using System.Diagnostics;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.SystemControl;
using Xunit.Abstractions;

namespace MasterHub.Core.Tests;

/// <summary>
/// Sonda wydajności usług działających w tle (CPU na tick). Uruchamiana ręcznie:
/// <c>dotnet test --filter PerfProbe</c> z ustawioną zmienną MH_PERF=1 - normalnie pomijana (zależy od sprzętu).
/// </summary>
public class PerfProbeTests
{
    private readonly ITestOutputHelper _out;
    public PerfProbeTests(ITestOutputHelper output) => _out = output;

    private static bool Enabled => Environment.GetEnvironmentVariable("MH_PERF") == "1";

    private static double CpuMs() => Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;

    private void Report(string name, double cpuMs, int ticks, double seconds)
    {
        string line = $"[PERF] {name}: {cpuMs:F1} ms CPU / {seconds:F0} s  => {cpuMs / Math.Max(1, ticks):F2} ms/tick, {cpuMs / (seconds * 10):F3} % of one core";
        _out.WriteLine(line);
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "mh_perf.txt"), line + Environment.NewLine);
    }

    [Fact]
    public async Task SystemMonitor_CostPerTick()
    {
        if (!Enabled) return;
        using var mon = new SystemMonitorService();
        await Task.Delay(1500);                       // rozgrzewka (JIT, pierwsze próbki)
        int ticks = 0; mon.MetricsUpdated += () => Interlocked.Increment(ref ticks);
        double c0 = CpuMs();
        await Task.Delay(10_000);
        Report("SystemMonitorService (1 s interval)", CpuMs() - c0, ticks, 10);
    }

    [Fact]
    public async Task SystemMonitor_Breakdown()
    {
        if (!Enabled) return;
        using var mon = new SystemMonitorService();
        mon.IsEnabled = false;
        await Task.Delay(500);
        var sample = typeof(SystemMonitorService).GetMethod("SampleMetricsInternal",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var scope in new[] { MonitorMetrics.All, MonitorMetrics.Ram, MonitorMetrics.None })
        {
            mon.RequiredMetrics = scope;
            sample.Invoke(mon, null);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) sample.Invoke(mon, null);
            string line = $"[PERF] SampleMetricsInternal RequiredMetrics={scope}: {sw.Elapsed.TotalMilliseconds / 100:F3} ms/tick";
            _out.WriteLine(line);
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "mh_perf.txt"), line + Environment.NewLine);
        }
    }

    [Fact]
    public async Task NowPlaying_CostPerTick()
    {
        if (!Enabled) return;
        using var np = new NowPlayingService();
        await Task.Delay(1500);
        double c0 = CpuMs();
        await Task.Delay(10_000);
        Report("NowPlayingService (1 s interval)", CpuMs() - c0, 10, 10);
    }

    [Fact]
    public async Task SmartProfile_CostPerTick()
    {
        if (!Enabled) return;
        using var sp = new SmartProfileService();
        await Task.Delay(1000);
        double c0 = CpuMs();
        await Task.Delay(10_000);
        Report("SmartProfileService (500 ms interval)", CpuMs() - c0, 20, 10);
    }

    [Fact]
    public void ProcessHelper_CostPerCall()
    {
        if (!Enabled) return;
        ProcessHelper.IsProcessRunning("obs64");
        double c0 = CpuMs();
        const int n = 50;
        for (int i = 0; i < n; i++) ProcessHelper.IsProcessRunning("obs64");
        Report("ProcessHelper.IsProcessRunning", CpuMs() - c0, n, n * 1.0 / 10);
    }
}

using System.Diagnostics;
using MasterHub.Core.SystemControl;

namespace MasterHub.Core.Tests;

public class ProcessHelperTests
{
    [Fact]
    public void DetectsOwnProcess_CaseAndExtensionInsensitive_AndRejectsUnknown()
    {
        string me = Process.GetCurrentProcess().ProcessName;

        Assert.True(ProcessHelper.IsProcessRunning(me));
        Assert.True(ProcessHelper.IsProcessRunning(me.ToUpperInvariant() + ".exe"));
        Assert.True(ProcessHelper.IsAnyProcessRunning("definitely_not_a_process_xyz", me));
        Assert.False(ProcessHelper.IsProcessRunning("definitely_not_a_process_xyz"));
        Assert.False(ProcessHelper.IsProcessRunning(""));
        Assert.False(ProcessHelper.IsAnyProcessRunning());
    }

    [Fact]
    public void ForcedRefresh_SeesNewlyStartedProcess_WhileCachedLookupsAreMuchCheaper()
    {
        string me = Process.GetCurrentProcess().ProcessName;
        ProcessHelper.InvalidateCache();
        ProcessHelper.IsProcessRunning(me, allowCached: false);   // rozgrzewka (JIT)

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 300; i++) ProcessHelper.IsProcessRunning(me, allowCached: false);
        double fresh = sw.Elapsed.TotalMilliseconds / 300;

        sw.Restart();
        for (int i = 0; i < 300; i++) ProcessHelper.IsProcessRunning(me);
        double cached = sw.Elapsed.TotalMilliseconds / 300;

        Console.WriteLine($"[PERF] ProcessHelper: fresh snapshot {fresh:F3} ms/call, cached {cached:F5} ms/call");
        Assert.True(cached < fresh, $"cache ({cached:F5} ms) powinien być tańszy niż świeży zrzut ({fresh:F3} ms)");

        // proces uruchomiony po zrzucie jest widoczny po wymuszeniu odświeżenia
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(ProcessHelper.IsProcessRunning("cmd", allowCached: false));
        child.WaitForExit(5000);
    }
}

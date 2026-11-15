using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Diagnostics;
using MasterHub.Core.SystemControl;
using Xunit;

namespace MasterHub.Core.Tests;

/// <summary>T-34 #4 (autostart entry renewed on every start) and #5 (outbound calls per host in the log).</summary>
public class StartupAndNetworkLogTests
{
    private const string Exe = @"C:\Apps\MasterHub\MasterHub.Avalonia.exe";

    [Fact]
    public void NoEntry_MeansAutostartIsOff_AndNothingIsWritten()
    {
        var writes = new List<string>();
        var r = AutostartEntry.Renew(null, Exe, writes.Add);

        Assert.False(r.Enabled);
        Assert.Empty(writes);
        Assert.Contains("off", AutostartEntry.Describe(r));
    }

    [Fact]
    public void AnEntryForAnOldFolder_IsRewritten_KeepingStartMinimised()
    {
        var writes = new List<string>();
        var r = AutostartEntry.Renew("\"D:\\Old\\MasterHub.Avalonia.exe\" --autostart --minimized", Exe, writes.Add);

        Assert.True(r.Enabled);
        Assert.True(r.Rewritten);
        Assert.Equal("\"" + Exe + "\" --autostart --minimized", Assert.Single(writes));
        Assert.Contains("renewed", AutostartEntry.Describe(r));
    }

    [Fact]
    public void AnUpToDateEntry_IsLeftAlone()
    {
        var writes = new List<string>();
        string current = AutostartEntry.BuildCommand(Exe, startMinimized: false);
        var r = AutostartEntry.Renew(current, Exe, writes.Add);

        Assert.True(r.Enabled);
        Assert.False(r.Rewritten);
        Assert.Empty(writes);
        Assert.False(AutostartEntry.IsMinimized(current));
    }

    [Fact]
    public void TheCommandFormat_IsTheOneTheAppAlwaysWrote()
    {
        // the format before T-34 #4 was "\"exe\" --autostart --minimized" – existing entries must count as up to date
        Assert.Equal("\"" + Exe + "\" --autostart --minimized", AutostartEntry.BuildCommand(Exe, true));
        Assert.Equal("\"" + Exe + "\" --autostart", AutostartEntry.BuildCommand(Exe, false));
    }

    [Fact]
    public void TheCounter_SumsCallsPerHost_AndStartsAgainAfterADrain()
    {
        using var counter = new OutboundCallCounter();
        counter.Record("api.github.com");
        counter.Record("API.GITHUB.COM");
        counter.Record("127.0.0.1");
        counter.Record(null);

        var counts = counter.Drain();
        Assert.Equal(("api.github.com", 2), counts[0]);
        Assert.Equal(3, counts.Count);
        Assert.Empty(counter.Drain());
    }

    [Fact]
    public void TheLogLine_ListsTheBusiestHostFirst_AndIsSkippedWithoutCalls()
    {
        Assert.Null(OutboundCallCounter.Format(Array.Empty<(string, int)>(), TimeSpan.FromMinutes(1)));
        string line = OutboundCallCounter.Format(new[] { ("open-meteo.com", 4), ("api.github.com", 1) }, TimeSpan.FromMinutes(1))!;

        Assert.Equal("[Network] 5 outbound call(s) in the last 60 s: open-meteo.com ×4, api.github.com ×1", line);
    }

    [Fact]
    public async Task RealHttpClientCalls_AreCounted_ThroughDotNetMetrics()
    {
        // a tiny local server so the test needs no network
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            for (int i = 0; i < 2; i++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                _ = await stream.ReadAsync(buffer);
                var reply = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
                await stream.WriteAsync(reply);
            }
        });

        using var counter = new OutboundCallCounter();
        var lines = new List<string>();
        counter.Start(lines.Add, TimeSpan.FromHours(1));   // the timer will not fire during the test; we drain by hand

        using (var http = new HttpClient())
        {
            await http.GetStringAsync($"http://127.0.0.1:{port}/a");
            await http.GetStringAsync($"http://127.0.0.1:{port}/b?token=secret");
        }
        await serve;
        listener.Stop();

        var counts = counter.Drain();
        Assert.Contains(counts, c => c.Host == "127.0.0.1" && c.Count >= 2);
        Assert.DoesNotContain(counts, c => c.Host.Contains("secret"));   // only host names are kept
    }

    [Fact]
    public void AppLog_KeepsRecentLines_AndTellsListeners()
    {
        AppLog.Clear();
        var heard = new List<string>();
        Action<DateTime, string> listener = (_, t) => heard.Add(t);
        AppLog.Written += listener;
        try
        {
            AppLog.Write("[Autostart] on – entry up to date");
            AppLog.Write("  ");   // blank: ignored
        }
        finally
        {
            AppLog.Written -= listener;
        }

        Assert.Equal(new[] { "[Autostart] on – entry up to date" }, heard);
        Assert.Contains(AppLog.Recent(), l => l.Text.StartsWith("[Autostart]"));
    }
}

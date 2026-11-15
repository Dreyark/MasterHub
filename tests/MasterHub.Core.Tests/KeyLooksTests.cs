using MasterHub.Core.Looks;

namespace MasterHub.Core.Tests;

/// <summary>T-12: conditional looks – layering, hysteresis, over-midnight time ranges, app matching and label tokens.</summary>
public class KeyLooksTests
{
    private sealed class Signals : ILookSignals
    {
        public double Cpu { get; set; }
        public double Ram { get; set; }
        public double Gpu { get; set; }
        public double CpuTemp { get; set; }
        public double GpuTemp { get; set; }
        public bool MusicPlaying { get; set; }
        public bool MicMuted { get; set; }
        public bool MasterMuted { get; set; }
        public string? ForegroundApp { get; set; }
        public string? Song { get; set; }
        public string? Artist { get; set; }
        public DateTime Now { get; set; } = new(2026, 11, 14, 12, 0, 0);
    }

    private static KeyLookRule Rule(LookConditionKind kind, double threshold = 80, string text = "", string? bg = null,
        string? fg = null, string? title = null, string? subtitle = null, bool enabled = true) =>
        new()
        {
            Enabled = enabled,
            When = new KeyLookCondition { Kind = kind, Threshold = threshold, Text = text },
            BackgroundColorHex = bg, TextColorHex = fg, Title = title, Subtitle = subtitle
        };

    // ── layering ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoRules_OrNoRuleHolding_LeavesTheKeyAlone()
    {
        var s = new Signals();
        var state = new KeyLookState();

        Assert.True(KeyLookEvaluator.Evaluate(null, s, state).IsNone);
        Assert.True(KeyLookEvaluator.Evaluate(Array.Empty<KeyLookRule>(), s, state).IsNone);
        Assert.True(KeyLookEvaluator.Evaluate(new[] { Rule(LookConditionKind.MicMuted, bg: "#FF0000") }, s, state).IsNone);
    }

    [Fact]
    public void Rules_AreLayers_LaterWins_FieldsLeftEmptyAreNotTouched()
    {
        var s = new Signals { MusicPlaying = true, MicMuted = true };
        var rules = new[]
        {
            Rule(LookConditionKind.MusicPlaying, bg: "#111111", fg: "#AAAAAA", title: "MUSIC"),
            Rule(LookConditionKind.MicMuted, bg: "#222222"),                  // overrides only the background
            Rule(LookConditionKind.MasterMuted, bg: "#333333")                // does not hold
        };

        var look = KeyLookEvaluator.Evaluate(rules, s, new KeyLookState());

        Assert.Equal("#222222", look.Background);   // the later rule won the field they both set
        Assert.Equal("#AAAAAA", look.Text);          // the earlier rule's other fields survive
        Assert.Equal("MUSIC", look.Title);
        Assert.Null(look.Subtitle);
        Assert.StartsWith("r0;r1;", look.Signature);
    }

    [Fact]
    public void DisabledRules_AreSkipped()
    {
        var s = new Signals { MicMuted = true };
        Assert.True(KeyLookEvaluator.Evaluate(new[] { Rule(LookConditionKind.MicMuted, bg: "#FF0000", enabled: false) }, s, new KeyLookState()).IsNone);
    }

    [Fact]
    public void Signature_ChangesOnlyWhenTheAppliedLookChanges()
    {
        var s = new Signals { MusicPlaying = true, Cpu = 10 };
        var rules = new[] { Rule(LookConditionKind.MusicPlaying, title: "CPU {cpu}") };
        var state = new KeyLookState();

        var a = KeyLookEvaluator.Evaluate(rules, s, state);
        var same = KeyLookEvaluator.Evaluate(rules, s, state);
        s.Cpu = 55;
        var changed = KeyLookEvaluator.Evaluate(rules, s, state);   // the filled label differs

        Assert.Equal(a.Signature, same.Signature);
        Assert.NotEqual(a.Signature, changed.Signature);
        Assert.Equal("CPU 55%", changed.Title);
    }

    // ── metrics & hysteresis ────────────────────────────────────────────────────────────────

    [Fact]
    public void Metric_TurnsOnAtTheLimit_AndOffOnlyBelowTheMargin()
    {
        var s = new Signals();
        var rules = new[] { Rule(LookConditionKind.CpuAbove, threshold: 80, bg: "#FF0000") };
        var state = new KeyLookState();

        bool On(double cpu) { s.Cpu = cpu; return !KeyLookEvaluator.Evaluate(rules, s, state).IsNone; }

        Assert.False(On(79));
        Assert.True(On(80));                 // reaches the limit
        Assert.True(On(78));                 // dips just below: stays on (no flicker)
        Assert.True(On(76));
        Assert.False(On(74));                // 5 below the limit: off
        Assert.False(On(79));                // and it needs the full limit again to come back
        Assert.True(On(81));
    }

    [Theory]
    [InlineData(LookConditionKind.CpuAbove)]
    [InlineData(LookConditionKind.RamAbove)]
    [InlineData(LookConditionKind.GpuAbove)]
    [InlineData(LookConditionKind.CpuTempAbove)]
    [InlineData(LookConditionKind.GpuTempAbove)]
    public void EveryMetricKind_ReadsItsOwnValue(LookConditionKind kind)
    {
        var s = new Signals { Cpu = 1, Ram = 1, Gpu = 1, CpuTemp = 1, GpuTemp = 1 };
        var rules = new[] { Rule(kind, threshold: 50, bg: "#FF0000") };
        Assert.True(KeyLookEvaluator.Evaluate(rules, s, new KeyLookState()).IsNone);

        switch (kind)
        {
            case LookConditionKind.CpuAbove: s.Cpu = 90; break;
            case LookConditionKind.RamAbove: s.Ram = 90; break;
            case LookConditionKind.GpuAbove: s.Gpu = 90; break;
            case LookConditionKind.CpuTempAbove: s.CpuTemp = 90; break;
            case LookConditionKind.GpuTempAbove: s.GpuTemp = 90; break;
        }
        Assert.False(KeyLookEvaluator.Evaluate(rules, s, new KeyLookState()).IsNone);
    }

    // ── app & time ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("obs64", "obs64.exe", true)]
    [InlineData("OBS64.EXE", "obs64", true)]
    [InlineData(@"C:\Program Files\obs\obs64.exe", "obs64", true)]
    [InlineData("chrome", "obs64", false)]
    [InlineData(null, "obs64", false)]
    [InlineData("obs64", "", false)]
    public void AppInFront_IgnoresPathExeAndCase(string? foreground, string wanted, bool expected)
    {
        var s = new Signals { ForegroundApp = foreground };
        var holds = !KeyLookEvaluator.Evaluate(new[] { Rule(LookConditionKind.AppInFront, text: wanted, bg: "#FF0000") }, s, new KeyLookState()).IsNone;
        Assert.Equal(expected, holds);
    }

    [Theory]
    [InlineData("09:00-17:00", 8, 59, false)]
    [InlineData("09:00-17:00", 9, 0, true)]
    [InlineData("09:00-17:00", 16, 59, true)]
    [InlineData("09:00-17:00", 17, 0, false)]
    [InlineData("22:00-06:00", 23, 30, true)]      // over midnight
    [InlineData("22:00-06:00", 3, 0, true)]
    [InlineData("22:00-06:00", 12, 0, false)]
    [InlineData("nonsense", 12, 0, false)]
    [InlineData("25:00-26:00", 12, 0, false)]
    public void TimeRange_IncludingOverMidnight(string range, int hour, int minute, bool expected) =>
        Assert.Equal(expected, KeyLookEvaluator.InTimeRange(new DateTime(2026, 11, 14, hour, minute, 0), range));

    // ── tokens ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tokens_AreSubstituted_AndUnknownOnesStay()
    {
        var s = new Signals { Cpu = 41.6, Ram = 63, Gpu = 7, CpuTemp = 65.2, GpuTemp = 70, Song = "Song", Artist = "Band" };

        Assert.Equal("CPU 42% RAM 63% GPU 7%", LabelTokens.Substitute("CPU {cpu} RAM {ram} GPU {gpu}", s));
        Assert.Equal("65°C / 70°C", LabelTokens.Substitute("{cpu_temp} / {gpu_temp}", s));
        Assert.Equal("12:00", LabelTokens.Substitute("{time}", s));
        Assert.Equal("Band – Song", LabelTokens.Substitute("{artist} – {song}", s));
        Assert.Equal("{unknown} 42%", LabelTokens.Substitute("{unknown} {CPU}", s));
        Assert.Equal("no tokens", LabelTokens.Substitute("no tokens", s));
    }

    [Fact]
    public void Needed_ListsTheGroupsALabelUses()
    {
        Assert.Equal(new[] { "cpu", "gpu_temp" }, LabelTokens.Needed("{cpu} and {gpu_temp}").ToArray());
        Assert.Empty(LabelTokens.Needed("plain"));
    }
}

/// <summary>T-12: what the keys on screen need the monitor to sample.</summary>
public class LookDemandTests
{
    private static KeyLookRule Rule(LookConditionKind kind, string? title = null, bool enabled = true) =>
        new() { Enabled = enabled, When = new KeyLookCondition { Kind = kind }, Title = title };

    [Fact]
    public void NoRulesAndPlainLabels_NeedNothing()
    {
        Assert.False(LookDemand.For(null, "Play", "").Any);
        Assert.False(LookDemand.For(Array.Empty<KeyLookRule>(), null, null).Any);
    }

    [Theory]
    [InlineData(LookConditionKind.CpuAbove, MasterHub.Core.Monitoring.MonitorMetrics.Cpu)]
    [InlineData(LookConditionKind.RamAbove, MasterHub.Core.Monitoring.MonitorMetrics.Ram)]
    [InlineData(LookConditionKind.GpuAbove, MasterHub.Core.Monitoring.MonitorMetrics.Gpu)]
    [InlineData(LookConditionKind.GpuTempAbove, MasterHub.Core.Monitoring.MonitorMetrics.Gpu)]
    public void MetricRules_TurnOnJustTheirOwnSampling(LookConditionKind kind, MasterHub.Core.Monitoring.MonitorMetrics expected)
    {
        var demand = LookDemand.For(new[] { Rule(kind) });
        Assert.Equal(expected, demand.Metrics);
        Assert.False(demand.NowPlaying);
    }

    [Fact]
    public void CpuTemperature_AlsoSamplesTheGpu_ForTheFallback()
    {
        var demand = LookDemand.For(new[] { Rule(LookConditionKind.CpuTempAbove) });
        Assert.Equal(MasterHub.Core.Monitoring.MonitorMetrics.CpuTemperature | MasterHub.Core.Monitoring.MonitorMetrics.Gpu, demand.Metrics);
    }

    [Fact]
    public void NonMetricConditions_NeedTheirOwnSource_NotTheMonitor()
    {
        Assert.True(LookDemand.For(new[] { Rule(LookConditionKind.MusicPlaying) }).NowPlaying);
        Assert.True(LookDemand.For(new[] { Rule(LookConditionKind.MicMuted) }).Audio);
        Assert.True(LookDemand.For(new[] { Rule(LookConditionKind.MasterMuted) }).Audio);
        Assert.True(LookDemand.For(new[] { Rule(LookConditionKind.AppInFront) }).Foreground);
        Assert.True(LookDemand.For(new[] { Rule(LookConditionKind.TimeBetween) }).Clock);

        var demand = LookDemand.For(new[] { Rule(LookConditionKind.AppInFront) });
        Assert.Equal(MasterHub.Core.Monitoring.MonitorMetrics.None, demand.Metrics);
        Assert.True(demand.Any);   // the timer still has to run
    }

    [Fact]
    public void DisabledRules_AreIgnored()
    {
        Assert.False(LookDemand.For(new[] { Rule(LookConditionKind.CpuAbove, enabled: false) }).Any);
    }

    [Fact]
    public void Tokens_InAnyLabel_TurnOnTheirSources()
    {
        Assert.Equal(MasterHub.Core.Monitoring.MonitorMetrics.Cpu | MasterHub.Core.Monitoring.MonitorMetrics.Ram,
            LookDemand.For(null, "{cpu}", "RAM {ram}").Metrics);
        Assert.True(LookDemand.For(null, "{artist} - {song}").NowPlaying);
        Assert.True(LookDemand.For(new[] { Rule(LookConditionKind.MicMuted, title: "{gpu_temp}") }).Metrics.HasFlag(MasterHub.Core.Monitoring.MonitorMetrics.Gpu));
    }

    [Fact]
    public void TimeAndDateTokens_NeedARedrawClock_ButNoSampling()
    {
        var demand = LookDemand.For(null, "{time}");
        Assert.True(demand.Clock);
        Assert.Equal(MasterHub.Core.Monitoring.MonitorMetrics.None, demand.Metrics);
        Assert.True(LookDemand.For(null, null, "{date}").Clock);
    }

    [Fact]
    public void Signature_ChangesWhenARulesColourIsEdited()
    {
        var s = new KeyLooksTests_Signals();
        var rule = Rule(LookConditionKind.MicMuted);
        rule.BackgroundColorHex = "#111111";
        s.MicMuted = true;
        var state = new KeyLookState();

        string before = KeyLookEvaluator.Evaluate(new[] { rule }, s, state).Signature;
        rule.BackgroundColorHex = "#222222";
        string after = KeyLookEvaluator.Evaluate(new[] { rule }, s, state).Signature;

        Assert.NotEqual(before, after);
    }

    private sealed class KeyLooksTests_Signals : ILookSignals
    {
        public double Cpu => 0; public double Ram => 0; public double Gpu => 0; public double CpuTemp => 0; public double GpuTemp => 0;
        public bool MusicPlaying => false;
        public bool MicMuted { get; set; }
        public bool MasterMuted => false;
        public string? ForegroundApp => null; public string? Song => null; public string? Artist => null;
        public DateTime Now => new(2026, 11, 14, 12, 0, 0);
    }
}

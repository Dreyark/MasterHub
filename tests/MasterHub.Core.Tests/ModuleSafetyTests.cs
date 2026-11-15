using MasterHub.Core.Native;

namespace MasterHub.Core.Tests;

/// <summary>T-35: firmware-wedge guard, software-mode handshake and "no builder emits a forbidden report".</summary>
public class ModuleSafetyTests
{
    // ── Feature-read guard ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0x01FE)] // Key Deck 15
    [InlineData(0x0206)] // Dial
    public void KeysAndDial_RefuseFeatureReads_UntilSoftwareMode(ushort pid)
    {
        var state = new ModuleSoftwareState(pid);
        Assert.True(state.RequiresSoftwareModeForReads);
        Assert.False(state.MayReadFeatures);

        state.OnSoftwareModeSent(true, result: 0);
        Assert.True(state.MayReadFeatures);

        state.OnSoftwareModeSent(false, result: 0);
        Assert.False(state.MayReadFeatures);
    }

    [Fact]
    public void FailedSoftwareModeSend_DoesNotChangeState()
    {
        var state = new ModuleSoftwareState(0x01FE);
        state.OnSoftwareModeSent(true, result: 7);
        Assert.False(state.IsSoftwareMode);
        Assert.False(state.MayReadFeatures);
    }

    [Theory]
    [InlineData(0x01F6)] // base
    [InlineData(0x0200)] // faders
    [InlineData(0x0202)] // knobs
    [InlineData(0x0204)] // rollers
    public void M0Family_AlwaysMayReadFeatures(ushort pid)
    {
        var state = new ModuleSoftwareState(pid);
        Assert.False(state.RequiresSoftwareModeForReads);
        Assert.True(state.MayReadFeatures);
    }

    [Fact]
    public void ReleasedModule_NeverReadsAgain()
    {
        var keys = new ModuleSoftwareState(0x01FE);
        keys.OnSoftwareModeSent(true, 0);
        keys.MarkReleased();
        Assert.True(keys.IsReleased);
        Assert.False(keys.IsSoftwareMode);
        Assert.False(keys.MayReadFeatures);

        var faders = new ModuleSoftwareState(0x0200);
        faders.MarkReleased();
        Assert.False(faders.MayReadFeatures);
    }

    [Fact]
    public void Handle_OnInvalidFile_ReportsDisconnected_NotReleased()
    {
        using var handle = new MasterHubDeviceHandle(
            new Microsoft.Win32.SafeHandles.SafeFileHandle(IntPtr.Zero, false), 0x2516, 0x0206, "t", "t");
        Assert.False(handle.IsSoftwareMode);
        Assert.Equal(7, handle.GetFeatureReport(new byte[32]));

        // Release marks the handle inert even when nothing could be sent, and is idempotent.
        handle.Release(0);
        Assert.True(handle.IsReleased);
        Assert.Equal(0, handle.Release(0));
    }

    // ── Handshake ───────────────────────────────────────────────────────────────────────────

    private sealed class ScriptedLink : IModuleHandshakeLink
    {
        private readonly Func<int, bool, int> _send; // (callNumber, enabled) -> result
        public readonly List<string> Calls = new();
        public bool? ConfirmResult;
        private int _n;

        public ScriptedLink(Func<int, bool, int> send) => _send = send;

        public int SetSoftwareMode(bool enabled)
        {
            int r = _send(++_n, enabled);
            Calls.Add($"{(enabled ? "sw" : "hw")}:{r}");
            return r;
        }

        public bool? Confirm()
        {
            Calls.Add("read");
            return ConfirmResult;
        }
    }

    [Fact]
    public void Handshake_Success_SendsTwiceWithSettleTimes()
    {
        var link = new ScriptedLink((_, _) => 0);
        var sleeps = new List<int>();

        var result = SoftwareModeHandshake.Run(link, sleeps.Add);

        Assert.True(result.Ready);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(new[] { "sw:0", "sw:0", "read" }, link.Calls);
        Assert.Equal(new[] { 600, 200 }, sleeps);
    }

    [Fact]
    public void Handshake_FailedSend_IsNeverFollowedByARead()
    {
        // every send fails
        var link = new ScriptedLink((_, _) => 7);

        var result = SoftwareModeHandshake.Run(link, _ => { });

        Assert.False(result.Ready);
        Assert.Equal(3, result.Attempts);
        Assert.DoesNotContain("read", link.Calls);
        // 3 attempts: sw (fail) + hw hand-back between attempts (2×)
        Assert.Equal(3, link.Calls.Count(c => c.StartsWith("sw")));
        Assert.Equal(2, link.Calls.Count(c => c.StartsWith("hw")));
    }

    [Fact]
    public void Handshake_AnswersOnlyOnThirdAttempt_ReturnsReady()
    {
        // First four sends (attempts 1 and 2) fail on the second send of each attempt; attempt 3 is fine.
        var link = new ScriptedLink((n, enabled) => enabled && (n is 2 or 5) ? 7 : 0);
        var sleeps = new List<int>();

        var result = SoftwareModeHandshake.Run(link, sleeps.Add);

        Assert.True(result.Ready);
        Assert.Equal(3, result.Attempts);
        Assert.Contains(500, sleeps); // pause between attempts
    }

    [Fact]
    public void Handshake_ConfirmationReadFails_CountsAsFailedAttempt()
    {
        var link = new ScriptedLink((_, _) => 0) { ConfirmResult = false };

        var result = SoftwareModeHandshake.Run(link, _ => { });

        Assert.False(result.Ready);
        Assert.Equal(3, link.Calls.Count(c => c == "read"));
    }

    [Fact]
    public void Handshake_NoVerifiedReadBack_TrustsAcceptedSends()
    {
        var link = new ScriptedLink((_, _) => 0) { ConfirmResult = null };
        Assert.True(SoftwareModeHandshake.Run(link, _ => { }).Ready);
    }

    // ── "No builder produces a forbidden report" ───────────────────────────────────────────

    [Fact]
    public void NoReportBuilder_ProducesAForbiddenReport()
    {
        var samples = new List<byte[]>();
        byte[] values = { 0, 1, 2, 7, 14, 15, 100, 127, 200, 254, 255 };

        samples.Add(ReportBuilders.SetSoftwareMode(true));
        samples.Add(ReportBuilders.SetSoftwareMode(false));
        samples.Add(ReportBuilders.SetDialSoftwareMode(true));
        samples.Add(ReportBuilders.SetDialSoftwareMode(false));
        samples.Add(ReportBuilders.LedSpeed(0));

        foreach (byte a in values)
        {
            samples.Add(ReportBuilders.KeyBacklight(a));
            samples.Add(ReportBuilders.DialBacklight(a));
            samples.Add(ReportBuilders.LedSpeed(a));
            foreach (byte b in values)
            {
                samples.Add(ReportBuilders.FillKeyScreen(a, b, a));
                samples.Add(ReportBuilders.FillDialScreen(a, b, b));
                samples.Add(ReportBuilders.FillSingleKey(a, b, a, b));
                samples.Add(ReportBuilders.LedBrightness(a, b, a));
                foreach (bool persist in new[] { false, true })
                {
                    samples.Add(ReportBuilders.LedMode(a, b, a, persist));
                    samples.Add(ReportBuilders.LedColor(a, b, a, b, a, persist));
                }
            }
        }

        foreach (byte opcode in new[] { ReportBuilders.OpcodeKeyPicture, ReportBuilders.OpcodePanoramaPicture, ReportBuilders.OpcodeDialPicture })
        {
            foreach (int len in new[] { 1, 311, 312, 313, 5000 })
            {
                var data = new byte[len];
                Array.Fill(data, (byte)0xFF);
                var report = ReportBuilders.NewPictureReport(opcode, 14);
                int remaining = len;
                ushort idx = 0;
                while (remaining > 0)
                {
                    int chunk = Math.Min(remaining, ReportBuilders.PictureChunkMax);
                    ReportBuilders.FillPictureChunk(report, idx++, data, len - remaining, chunk, remaining <= ReportBuilders.PictureChunkMax);
                    samples.Add((byte[])report.Clone());
                    remaining -= chunk;
                }
            }
        }

        Assert.NotEmpty(samples);
        foreach (var sample in samples)
        {
            Assert.False(MasterHubDeviceHandle.IsForbiddenReport(sample),
                $"builder produced forbidden report 0x{sample[0]:X2} 0x{sample[1]:X2}");
        }
    }

    [Fact]
    public void Builders_KeepSafeBacklightMinimum()
    {
        Assert.Equal(1, ReportBuilders.KeyBacklight(0)[2]);
        Assert.Equal(1, ReportBuilders.DialBacklight(0)[2]);
        Assert.Equal(100, ReportBuilders.KeyBacklight(255)[2]);
    }

    [Fact]
    public void PictureChunk_LayoutMatchesProtocol()
    {
        var data = Enumerable.Range(0, 400).Select(i => (byte)i).ToArray();
        var report = ReportBuilders.NewPictureReport(ReportBuilders.OpcodeKeyPicture, 9);

        ReportBuilders.FillPictureChunk(report, 0, data, 0, 312, false);
        Assert.Equal(new byte[] { 0x02, 0x36, 0, 0, 0x38, 0x01, 9, 0 }, report[..8]);
        Assert.Equal(data[0], report[8]);

        ReportBuilders.FillPictureChunk(report, 1, data, 312, 88, true);
        Assert.Equal(1, report[2]);
        Assert.Equal(88, report[4]);
        Assert.Equal(1, report[7]);
        Assert.Equal(data[312], report[8]);
        Assert.Equal(0, report[8 + 88]); // tail is zeroed
        Assert.Equal(320, report.Length);
    }
}

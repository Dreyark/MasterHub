using MasterHub.Core.Actions;
using MasterHub.Core.Devices;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Native;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Tests;

public class SmallUnitTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("null", false)]   // a sandboxed iframe / data: URL of a foreign page
    [InlineData("http://127.0.0.1:28123", true)]
    [InlineData("http://localhost:28123", true)]
    [InlineData("https://evil.example.com", false)]
    [InlineData("http://127.0.0.1.evil.com", false)]
    [InlineData("garbage", false)]
    public void WebSocketOrigin_OnlyLoopbackOrNativeClientsAreTrusted(string? origin, bool trusted)
    {
        Assert.Equal(trusted, StreamDeckWebSocketServer.IsTrustedOrigin(origin));
    }

    [Theory]
    [InlineData(0x02, 0x2F)] // kalibracja czujnika bazy
    [InlineData(0x02, 0x2A)] // firmware
    [InlineData(0x02, 0x38)] // zapis obrazu do flash (Keys)
    [InlineData(0x02, 0x47)] // zapis obrazu do flash (Dial)
    public void SafetyGuard_BlocksDangerousReports(int id, int cmd)
    {
        Assert.True(MasterHubDeviceHandle.IsForbiddenReport(new byte[] { (byte)id, (byte)cmd, 0 }));
    }

    [Theory]
    [InlineData(0x02, 0x36)] // JPEG klawisza
    [InlineData(0x02, 0x35)] // panorama
    [InlineData(0x02, 0x2D)] // kolor LED
    public void SafetyGuard_AllowsNormalReports(int id, int cmd)
    {
        Assert.False(MasterHubDeviceHandle.IsForbiddenReport(new byte[] { (byte)id, (byte)cmd, 0 }));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(2, 0, 0)]
    [InlineData(98, 0, 100)]
    [InlineData(100, 50, 100)]
    [InlineData(50, 50, 50)]
    [InlineData(51, 50, 50)] // szum < 2 jest tłumiony
    [InlineData(53, 50, 53)]
    public void FaderFilter_AppliesDeadbandAndNoiseSuppression(int raw, int last, int expected)
    {
        Assert.Equal((byte)expected, MasterHubManager.FilterFaderValue((byte)raw, (byte)last));
    }

    [Fact]
    public void SmartProfileRule_MatchesProcessIgnoringExeAndCase_AndOptionalTitle()
    {
        var rule = new SmartProfileRule { ProcessName = "OBS64.exe", ProfileId = "p" };
        Assert.True(rule.Matches("obs64", "anything"));
        Assert.False(rule.Matches("spotify", "anything"));

        rule.WindowTitle = "Studio";
        Assert.True(rule.Matches("obs64", "OBS Studio 30"));
        Assert.False(rule.Matches("obs64", "Settings"));

        rule.IsEnabled = false;
        Assert.False(rule.Matches("obs64", "OBS Studio"));
    }

    [Fact]
    public void ConditionalAction_JsonRoundTripAndSimpleFormat()
    {
        var model = new ConditionalActionModel
        {
            Condition = ConditionType.FileExists,
            Target = @"C:\x",
            IfTrueAction = DeckActionType.OpenWebsite,
            IfTrueParameter = "https://a.b"
        };
        var parsed = ConditionalActionModel.Parse(model.ToParameterString());
        Assert.Equal(ConditionType.FileExists, parsed.Condition);
        Assert.Equal("https://a.b", parsed.IfTrueParameter);

        var simple = ConditionalActionModel.Parse("obs64?Calculator:Browser");
        Assert.Equal("obs64", simple.Target);
        Assert.Equal(DeckActionType.Calculator, simple.IfTrueAction);
        Assert.Equal(DeckActionType.Browser, simple.IfFalseAction);

        Assert.Equal(DeckActionType.None, ConditionalActionModel.Parse("{broken").IfTrueAction);
    }

    [Fact]
    public void KeyEngine_OnPress_TriggersOnceAndSurvivesThrowingHandler()
    {
        using var engine = new KeyInteractionEngine();
        int calls = 0;
        engine.KeyTriggered += (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("boom");
        };

        engine.HandleKeyEvent(3, true, KeyPressMode.OnPress);
        engine.HandleKeyEvent(3, true, KeyPressMode.OnPress); // powtórzony stan - bez drugiego wyzwolenia
        engine.HandleKeyEvent(3, false, KeyPressMode.OnPress);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task KeyEngine_HoldRepeat_StopsAfterRelease_WithoutThrowing()
    {
        using var engine = new KeyInteractionEngine { HoldRepeatInitialDelayMs = 20, HoldRepeatIntervalMs = 10 };
        int calls = 0;
        engine.KeyTriggered += (_, _) => Interlocked.Increment(ref calls);

        engine.HandleKeyEvent(1, true, KeyPressMode.OnHoldRepeat);
        await Task.Delay(150);
        engine.HandleKeyEvent(1, false, KeyPressMode.OnHoldRepeat);
        int afterRelease = Volatile.Read(ref calls);
        await Task.Delay(80);

        Assert.True(afterRelease >= 2, $"oczekiwano powtórzeń, było {afterRelease}");
        Assert.Equal(afterRelease, Volatile.Read(ref calls));
    }
}

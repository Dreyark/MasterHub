using System.Buffers.Binary;
using System.Text;
using MasterHub.Core.Integrations.Discord;

namespace MasterHub.Core.Tests.Integrations;

/// <summary>
/// Format ramek Discord IPC (https://discord.com/developers/docs/topics/rpc): [opcode u32 LE][length u32 LE][JSON UTF-8],
/// opcodes: 0 HANDSHAKE, 1 FRAME, 2 CLOSE, 3 PING, 4 PONG. (Test nie łączy się z potokiem - nie dotyka działającego Discorda.)
/// </summary>
public class DiscordProtocolTests
{
    [Fact]
    public void Handshake_Frame_HasExactSpecLayout()
    {
        string json = "{\"v\":1,\"client_id\":\"123\"}";

        byte[] frame = DiscordService.EncodeFrame(DiscordIpcOpcode.Handshake, json);

        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)));
        Assert.Equal((uint)Encoding.UTF8.GetByteCount(json), BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4, 4)));
        Assert.Equal(json, Encoding.UTF8.GetString(frame, 8, frame.Length - 8));
    }

    [Theory]
    [InlineData(DiscordIpcOpcode.Handshake, "{}")]
    [InlineData(DiscordIpcOpcode.Frame, "{\"cmd\":\"SET_VOICE_SETTINGS\",\"args\":{\"mute\":true},\"nonce\":\"n1\"}")]
    [InlineData(DiscordIpcOpcode.Frame, "{\"opis\":\"zażółć gęślą jaźń 🎙\"}")]
    [InlineData(DiscordIpcOpcode.Ping, "{}")]
    public void Frame_RoundTrips_IncludingNonAscii(DiscordIpcOpcode op, string json)
    {
        Assert.True(DiscordService.TryDecodeFrame(DiscordService.EncodeFrame(op, json), out var decodedOp, out var decodedJson));
        Assert.Equal(op, decodedOp);
        Assert.Equal(json, decodedJson);
    }

    [Fact]
    public void Decode_RejectsUnknownOpcode_Oversized_AndTruncatedFrames()
    {
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 99);
        Assert.False(DiscordService.TryDecodeFrameHeader(header, out _, out _));

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), uint.MaxValue);
        Assert.False(DiscordService.TryDecodeFrameHeader(header, out _, out _));

        byte[] full = DiscordService.EncodeFrame(DiscordIpcOpcode.Frame, "{\"a\":1}");
        Assert.False(DiscordService.TryDecodeFrame(full.AsSpan(0, full.Length - 2), out _, out _));
    }

    [Theory]
    [InlineData("https://discord.com/channels/111/222", "111", "222")]
    [InlineData("discord://-/channels/111/222", "111", "222")]
    [InlineData("111/222", "111", "222")]
    [InlineData("222", null, "222")]
    [InlineData("  https://discord.com/channels/111/222/  ", "111", "222")]
    public void ExtractGuildAndChannelId_HandlesCommonInputs(string input, string? guild, string? channel)
    {
        Assert.Equal((guild, channel), DiscordService.ExtractGuildAndChannelId(input));
    }

    [Fact]
    public async Task CameraAndScreenShare_NeverClaimSuccess_BecauseRpcHasNoSuchCommands()
    {
        using var discord = new DiscordService();   // nie połączony

        Assert.False(await discord.SetCameraAsync(true));
        Assert.False(await discord.SetScreenShareAsync(true));
    }
}

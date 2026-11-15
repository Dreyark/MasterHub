using System.Net;
using System.Reflection;
using System.Text.Json;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.Twitch;

namespace MasterHub.Core.Tests.Integrations;

public class TwitchKickIntegrationTests
{
    // ---------------------------------------------------------------- pomocnicze (stan prywatny serwisu)

    private static void Set(object target, string member, object? value)
    {
        var t = target.GetType();
        var f = t.GetField(member, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f != null) { f.SetValue(target, value); return; }
        var p = t.GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(t.Name, member);
        p.GetSetMethod(true)!.Invoke(target, new[] { value });
    }

    private static TwitchService NewTwitch(RecordingHandler handler, string channel = "streamer")
    {
        var tw = new TwitchService(new HttpClient(handler));
        Set(tw, "_isConnected", true);
        Set(tw, "ChannelName", channel);
        Set(tw, "_cachedToken", "tok");
        return tw;
    }

    // ---------------------------------------------------------------- Twitch

    [Theory]
    [InlineData("PING :tmi.twitch.tv", "PING")]
    [InlineData(":tmi.twitch.tv 001 nick :Welcome, GLHF!", "001")]
    [InlineData(":nick.tmi.twitch.tv 353 nick = #chan :nick", "353")]
    [InlineData("@emote-only=1;slow=0 :tmi.twitch.tv ROOMSTATE #chan", "ROOMSTATE")]
    [InlineData("@badges=;color=#FF0000 :viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #chan :NOTICE emote_only_on", "PRIVMSG")]
    [InlineData("@msg-id=emote_only_on :tmi.twitch.tv NOTICE #chan :This room is now in emote-only mode.", "NOTICE")]
    public void Irc_CommandIsReadFromLineStructure_NotFromMessageText(string line, string expected)
    {
        Assert.Equal(expected, TwitchService.GetIrcCommand(line));
    }

    [Fact]
    public async Task Twitch_ChatModes_UseHelixPatchWithModeratorId_NotRemovedIrcCommands()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, "{}"));
        using var tw = NewTwitch(handler);
        tw.SetHelixCredentials("client-1", "1111", "oauth:tok");
        Set(tw, "_cachedUserId", "2222");   // właściciel tokena (moderator) != kanał

        Assert.True(await tw.SetEmoteOnlyAsync(true));
        Assert.True(await tw.SetSubOnlyAsync(true));
        Assert.True(await tw.SetSlowModeAsync(true, 500));    // przycięte do 120 s (limit Helix)
        Assert.True(await tw.SetSlowModeAsync(false));

        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("PATCH", r.Method.Method);
            Assert.StartsWith("https://api.twitch.tv/helix/chat/settings?broadcaster_id=1111&moderator_id=2222", r.Url);
            Assert.Equal("client-1", r.Headers["Client-Id"]);
            Assert.Equal("Bearer tok", r.Headers["Authorization"]);
        });
        Assert.True(JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("emote_mode").GetBoolean());
        Assert.True(JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("subscriber_mode").GetBoolean());
        var slow = JsonDocument.Parse(handler.Requests[2].Body).RootElement;
        Assert.True(slow.GetProperty("slow_mode").GetBoolean());
        Assert.Equal(120, slow.GetProperty("slow_mode_wait_time").GetInt32());
        Assert.False(JsonDocument.Parse(handler.Requests[3].Body).RootElement.GetProperty("slow_mode").GetBoolean());
        Assert.True(tw.IsEmoteOnly);
        Assert.True(tw.IsSubOnly);
        Assert.False(tw.IsSlowMode);
    }

    [Fact]
    public async Task Twitch_ChatMode_WhenApiRejects_DoesNotFlipLocalState()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.Unauthorized, "{\"status\":401}"));
        using var tw = NewTwitch(handler);
        tw.SetHelixCredentials("client-1", "1111");

        Assert.False(await tw.SetEmoteOnlyAsync(true));
        Assert.False(tw.IsEmoteOnly);
    }

    [Fact]
    public async Task Twitch_ClearChat_UsesHelixDeleteModerationChat()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.NoContent, ""));
        using var tw = NewTwitch(handler);
        tw.SetHelixCredentials("client-1", "1111");

        Assert.True(await tw.ClearChatAsync());

        var req = Assert.Single(handler.Requests);
        Assert.Equal("DELETE", req.Method.Method);
        Assert.StartsWith("https://api.twitch.tv/helix/moderation/chat?broadcaster_id=1111&moderator_id=1111", req.Url);
    }

    [Fact]
    public async Task Twitch_Clip_IsCreatedForTheChannel_NotForTheBotAccountOwningTheToken()
    {
        var handler = new RecordingHandler(r =>
        {
            if (r.Url.StartsWith("https://id.twitch.tv/oauth2/validate"))
                return (HttpStatusCode.OK, "{\"client_id\":\"cid\",\"login\":\"mybot\",\"user_id\":\"555\"}");
            if (r.Url.StartsWith("https://api.twitch.tv/helix/users?login=streamer"))
                return (HttpStatusCode.OK, "{\"data\":[{\"id\":\"999\",\"login\":\"streamer\"}]}");
            if (r.Url.StartsWith("https://api.twitch.tv/helix/clips"))
                return (HttpStatusCode.Accepted, "{\"data\":[{\"id\":\"ClipId\",\"edit_url\":\"https://clips.twitch.tv/ClipId/edit\"}]}");
            return (HttpStatusCode.NotFound, "{}");
        });
        using var tw = NewTwitch(handler, "streamer");

        var res = await tw.CreateClipAsync();

        Assert.True(res!.Success, res.ErrorMessage);
        var clipReq = handler.Requests.Single(r => r.Url.Contains("/helix/clips"));
        Assert.Contains("broadcaster_id=999", clipReq.Url);   // ID kanału, nie 555 (konto bota)
    }

    [Fact]
    public async Task Twitch_Clip_WhenTokenOwnerIsTheChannel_SkipsUserLookup()
    {
        var handler = new RecordingHandler(r =>
        {
            if (r.Url.StartsWith("https://id.twitch.tv/oauth2/validate"))
                return (HttpStatusCode.OK, "{\"client_id\":\"cid\",\"login\":\"streamer\",\"user_id\":\"777\"}");
            return (HttpStatusCode.Accepted, "{\"data\":[{\"id\":\"C\",\"edit_url\":\"u\"}]}");
        });
        using var tw = NewTwitch(handler, "Streamer");

        Assert.True((await tw.CreateClipAsync())!.Success);
        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("/helix/users"));
        Assert.Contains("broadcaster_id=777", handler.Requests.Last().Url);
    }

    // ---------------------------------------------------------------- Kick

    private static KickService NewKick(RecordingHandler handler, string? token)
    {
        var kick = new KickService(new HttpClient(handler));
        Set(kick, "ChannelName", "streamer");
        Set(kick, "_cachedAuthToken", token);
        return kick;
    }

    [Fact]
    public async Task Kick_SendChat_UsesOfficialApi_AndReportsRealResult()
    {
        var handler = new RecordingHandler(r => r.Url.Contains("/public/v1/channels")
            ? (HttpStatusCode.OK, "{\"data\":[{\"broadcaster_user_id\":4242,\"slug\":\"streamer\"}]}")
            : (HttpStatusCode.OK, "{\"data\":{\"is_sent\":true}}"));
        using var kick = NewKick(handler, "kick-token");

        Assert.True(await kick.SendChatMessageAsync("  hello  "));

        var post = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("https://api.kick.com/public/v1/chat", post.Url);
        Assert.Equal("Bearer kick-token", post.Headers["Authorization"]);
        var body = JsonDocument.Parse(post.Body).RootElement;
        Assert.Equal(4242, body.GetProperty("broadcaster_user_id").GetInt64());
        Assert.Equal("hello", body.GetProperty("content").GetString());
        Assert.Equal("user", body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Kick_SendChat_WithoutToken_DoesNotPretendToSucceed()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, "{}"));
        using var kick = NewKick(handler, null);

        Assert.False(await kick.SendChatMessageAsync("hello"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Kick_SendChat_ServerError_ReturnsFalse()
    {
        var handler = new RecordingHandler(r => r.Url.Contains("/channels")
            ? (HttpStatusCode.OK, "{\"data\":[{\"broadcaster_user_id\":1}]}")
            : (HttpStatusCode.Forbidden, "{}"));
        using var kick = NewKick(handler, "t");

        Assert.False(await kick.SendChatMessageAsync("hello"));
    }

    [Fact]
    public async Task Kick_UnsupportedOperations_ReturnFalse_AndDoNotChangeState_OrSpamChat()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, "{}"));
        using var kick = NewKick(handler, "t");

        Assert.False(await kick.ToggleEmoteOnlyAsync());
        Assert.False(await kick.ToggleSubOnlyAsync());
        Assert.False(await kick.ToggleSlowModeAsync(30));
        Assert.False(await kick.ClearChatAsync());
        Assert.False(kick.IsEmoteOnly || kick.IsSubOnly || kick.IsSlowMode);
        Assert.Empty(handler.Requests);   // wcześniej na czat szły dosłowne teksty "/slow 30", "/emoteonly"...

        var clip = await kick.CreateClipAsync();
        Assert.False(clip!.Success);
        Assert.False(string.IsNullOrWhiteSpace(clip.ErrorMessage));
    }

    [Fact]
    public void Kick_ChatroomUpdatedEvent_UpdatesModeState()
    {
        using var kick = NewKick(new RecordingHandler(_ => (HttpStatusCode.OK, "{}")), "t");

        kick.ApplyChatroomUpdate("{\"emotes_mode\":{\"enabled\":true},\"subscribers_mode\":{\"enabled\":true},\"slow_mode\":{\"enabled\":true,\"message_interval\":45}}");

        Assert.True(kick.IsEmoteOnly);
        Assert.True(kick.IsSubOnly);
        Assert.True(kick.IsSlowMode);
        Assert.Equal(45, kick.SlowModeDurationSeconds);

        kick.ApplyChatroomUpdate("{\"emotes_mode\":{\"enabled\":false},\"slow_mode\":{\"enabled\":false,\"message_interval\":0}}");
        Assert.False(kick.IsEmoteOnly);
        Assert.False(kick.IsSlowMode);
    }
}

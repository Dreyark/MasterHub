using System.Net;
using MasterHub.Core.Integrations.YouTube;

namespace MasterHub.Core.Tests.Integrations;

public class YouTubeIntegrationTests
{
    private static readonly string ChannelId = "UC" + new string('x', 22);

    private static RecordingHandler Handler(Func<bool> liveNow) => new(r =>
    {
        string u = r.Url;
        if (u.Contains("/channels?")) return (HttpStatusCode.OK, $"{{\"items\":[{{\"id\":\"{ChannelId}\",\"snippet\":{{\"title\":\"Streamer\"}}}}]}}");
        if (u.Contains("/playlistItems?")) return (HttpStatusCode.OK, "{\"items\":[{\"contentDetails\":{\"videoId\":\"vidLive0001\"}},{\"contentDetails\":{\"videoId\":\"vidOld00002\"}}]}");
        if (u.Contains("/videos?part=snippet&id="))
            return (HttpStatusCode.OK, "{\"items\":[{\"id\":\"vidOld00002\",\"snippet\":{\"liveBroadcastContent\":\"none\"}}," +
                                       $"{{\"id\":\"vidLive0001\",\"snippet\":{{\"liveBroadcastContent\":\"{(liveNow() ? "live" : "none")}\"}}}}]}}");
        if (u.Contains("/videos?part=liveStreamingDetails"))
            return (HttpStatusCode.OK, liveNow()
                ? "{\"items\":[{\"snippet\":{\"title\":\"Live now!\",\"channelTitle\":\"Streamer\",\"liveBroadcastContent\":\"live\"}," +
                  "\"liveStreamingDetails\":{\"concurrentViewers\":\"123\",\"actualStartTime\":\"2025-01-01T10:00:00Z\"},\"statistics\":{\"likeCount\":\"5\"}}]}"
                : "{\"items\":[{\"snippet\":{\"title\":\"Ended\",\"channelTitle\":\"Streamer\",\"liveBroadcastContent\":\"none\"}," +
                  "\"liveStreamingDetails\":{\"actualStartTime\":\"2025-01-01T10:00:00Z\",\"actualEndTime\":\"2025-01-01T12:00:00Z\"}}]}");
        return (HttpStatusCode.NotFound, "{}");
    });

    [Fact]
    public async Task DataApi_FindsLiveStream_WithoutTheExpensiveSearchEndpoint()
    {
        var handler = Handler(() => true);
        using var yt = new YouTubeService(handler);

        var status = await yt.FetchStreamStatusAsync("@streamer", "API-KEY");

        Assert.True(status.Success, status.ErrorMessage);
        Assert.True(status.IsLive);
        Assert.Equal(123, status.ConcurrentViewers);
        Assert.Equal(5, status.LikeCount);
        Assert.Equal("vidLive0001", status.VideoId);
        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("/search?"));   // search = 100 jednostek limitu
        Assert.All(handler.Requests, r => Assert.Contains("key=API-KEY", r.Url));
    }

    [Fact]
    public async Task DataApi_RepeatedPolls_ReuseChannelAndLiveVideo_CostingOneRequestEach()
    {
        var handler = Handler(() => true);
        using var yt = new YouTubeService(handler);
        await yt.FetchStreamStatusAsync("@streamer", "K");
        int afterFirst = handler.Requests.Count;

        for (int i = 0; i < 3; i++) await yt.FetchStreamStatusAsync("@streamer", "K");

        // kolejne odpytania: tylko videos (bez channels / playlistItems / search)
        var later = handler.Requests.Skip(afterFirst).ToList();
        Assert.Equal(3, later.Count);
        Assert.All(later, r => Assert.Contains("/videos?part=liveStreamingDetails", r.Url));
    }

    [Fact]
    public async Task DataApi_WhenStreamEnds_ReportsOffline_AndResumesDiscovery()
    {
        bool live = true;
        var handler = Handler(() => live);
        using var yt = new YouTubeService(handler);
        Assert.True((await yt.FetchStreamStatusAsync("@streamer", "K")).IsLive);

        live = false;
        var ended = await yt.FetchStreamStatusAsync("@streamer", "K");
        Assert.True(ended.Success);
        Assert.False(ended.IsLive);
        Assert.Equal(0, ended.ConcurrentViewers);

        int before = handler.Requests.Count;
        var again = await yt.FetchStreamStatusAsync("@streamer", "K");   // cache wyczyszczony -> znowu playlistItems
        Assert.False(again.IsLive);
        Assert.Contains(handler.Requests.Skip(before), r => r.Url.Contains("/playlistItems?"));
    }

    [Fact]
    public async Task DataApi_ApiError_IsReportedNotHiddenAsOffline()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":403,\"message\":\"quota exceeded\",\"errors\":[{\"reason\":\"quotaExceeded\"}]}}"));
        using var yt = new YouTubeService(handler);

        var status = await yt.FetchStreamStatusAsync(ChannelId, "K");

        Assert.False(status.Success);
        Assert.False(string.IsNullOrWhiteSpace(status.ErrorMessage));
    }
}

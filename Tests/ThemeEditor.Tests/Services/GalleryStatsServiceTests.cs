using System.Net;
using System.Net.Http;
using System.Text;
using ThemeEditorCSharp.Services;

namespace ThemeEditorCSharp.Tests.Services;

public sealed class GalleryStatsServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content != null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static (GalleryStatsService Service, FakeHandler Handler) Create(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(body, status);
        return (new GalleryStatsService(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task LoadStats_parses_themes_and_builds_escaped_url()
    {
        var (service, handler) = Create("""
            {"themes":[
              {"id":"a","downloads":5,"voteCount":2,"averageRating":4.5,"userRating":3},
              {"id":"b"}
            ]}
            """);

        var result = await service.LoadStatsAsync("https://api.example.test/v1/", "key with/space");

        Assert.Equal(2, result.Count);
        Assert.Equal(new GalleryThemeStats("a", 5, 2, 4.5, 3), result[0]);
        Assert.Equal(new GalleryThemeStats("b", 0, 0, 0, 0), result[1]);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.example.test/v1/themes/stats?voterKey=key%20with%2Fspace", handler.Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task LoadStats_skips_entries_without_id()
    {
        var (service, _) = Create("""{"themes":[{"downloads":1},{"id":"  "},{"id":"ok"},{"id":7}]}""");

        var result = await service.LoadStatsAsync("https://x.test", "k");

        Assert.Equal(new[] { "ok" }, result.Select(s => s.Id));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"themes":{"id":"a"}}""")]
    [InlineData("""{"themes":null}""")]
    public async Task LoadStats_without_themes_array_returns_empty(string json)
    {
        var (service, _) = Create(json);

        Assert.Empty(await service.LoadStatsAsync("https://x.test", "k"));
    }

    [Fact]
    public async Task LoadStats_accepts_numbers_encoded_as_strings_and_ignores_bad_ones()
    {
        var (service, _) = Create("""{"themes":[{"id":"a","downloads":"12","voteCount":"x","averageRating":"3.5","userRating":2.5}]}""");

        var stats = Assert.Single(await service.LoadStatsAsync("https://x.test", "k"));

        Assert.Equal(12, stats.Downloads);
        Assert.Equal(0, stats.VoteCount);
        Assert.Equal(3.5, stats.AverageRating);
        Assert.Equal(0, stats.UserRating);
    }

    [Fact]
    public async Task LoadStats_throws_on_http_error()
    {
        var (service, _) = Create("{}", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.LoadStatsAsync("https://x.test", "k"));
    }

    [Fact]
    public async Task SubmitVote_posts_json_and_parses_response()
    {
        var (service, handler) = Create("""{"id":"theme/1","downloads":3,"voteCount":4,"averageRating":4.25,"userRating":5}""");

        var stats = await service.SubmitVoteAsync("https://x.test/api/", "theme/1", "voter", 5);

        Assert.Equal(new GalleryThemeStats("theme/1", 3, 4, 4.25, 5), stats);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://x.test/api/themes/theme%2F1/vote", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("""{"voterKey":"voter","rating":5}""", handler.RequestBody);
    }

    [Fact]
    public async Task SubmitVote_uses_requested_id_when_response_has_none()
    {
        var (service, _) = Create("""{"voteCount":1}""");

        var stats = await service.SubmitVoteAsync("https://x.test", "abc", "v", 3);

        Assert.Equal("abc", stats.Id);
        Assert.Equal(1, stats.VoteCount);
    }

    [Fact]
    public async Task SubmitVote_throws_on_http_error()
    {
        var (service, _) = Create("{}", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.SubmitVoteAsync("https://x.test", "a", "v", 1));
    }

    [Fact]
    public async Task NotifyDownload_posts_empty_object_and_parses_response()
    {
        var (service, handler) = Create("""{"id":"a","downloads":10}""");

        var stats = await service.NotifyDownloadAsync("https://x.test/", "a");

        Assert.Equal(10, stats.Downloads);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://x.test/themes/a/download", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("{}", handler.RequestBody);
    }

    [Fact]
    public async Task NotifyDownload_throws_on_http_error()
    {
        var (service, _) = Create("{}", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.NotifyDownloadAsync("https://x.test", "a"));
    }
}

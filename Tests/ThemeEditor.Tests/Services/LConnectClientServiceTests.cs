using System.Net;
using System.Net.Http;
using System.Text;
using ThemeEditorCSharp.Services;
using Mode = ThemeEditorCSharp.Services.LConnectClientService.LConnectRequestMode;

namespace ThemeEditorCSharp.Tests.Services;

// Pins the behaviour merged from Phone Link's former PhoneControl.LConnectClient copy.
// No test here may reach port discovery: it scans ProgramData/LocalAppData for L-Connect settings.
public sealed class LConnectClientServiceTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<(Uri Uri, byte[] Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content == null
                ? Array.Empty<byte>()
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add((request.RequestUri!, body));
            return _respond(request);
        }
    }

    private static HttpResponseMessage Respond(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("RevertTemplateBackground")]
    [InlineData("Ping")]
    [InlineData("StopVideo")]
    public async Task OfficialCompatible_sends_empty_body_for_empty_body_actions(string type)
    {
        using var content = LConnectClientService.CreateContent(type, "{}", Mode.OfficialCompatible);

        Assert.Empty(await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("application/json", content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Legacy_keeps_the_json_body_for_empty_body_actions()
    {
        using var content = LConnectClientService.CreateContent("RevertTemplateBackground", "{}", Mode.Legacy);

        Assert.Equal("{}", await content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OfficialCompatible_keeps_a_non_empty_body()
    {
        using var content = LConnectClientService.CreateContent("RevertTemplateBackground", "{\"id\":1}", Mode.OfficialCompatible);

        Assert.Equal("{\"id\":1}", await content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("LWireless", true)]
    [InlineData("lwireless", true)]
    [InlineData("SyncControllerList", true)]
    [InlineData("Ping", false)]
    [InlineData("GetTemplates", false)]
    public void AcceptsEmptyResponse_includes_LWireless(string action, bool expected) =>
        Assert.Equal(expected, LConnectClientService.AcceptsEmptyResponse(action));

    [Fact]
    public void RequiresEmptyRequestBody_includes_RevertTemplateBackground() =>
        Assert.True(LConnectClientService.RequiresEmptyRequestBody("RevertTemplateBackground"));

    [Theory]
    [InlineData("Device", "FanLightingSetting", true)]
    [InlineData("Service", "FanMergeLightingSetting", true)]
    [InlineData("Service", "LWireless", true)]
    [InlineData("Device", "LWireless", false)]
    [InlineData("Service", "Ping", false)]
    public void Body_trace_covers_only_fan_lighting(string channel, string type, bool expected) =>
        Assert.Equal(expected, LConnectClientService.ShouldLogBody(channel, type));

    [Fact]
    public void BuildServiceUrl_appends_escaped_query_and_drops_action_overrides()
    {
        var query = new Dictionary<string, string>
        {
            ["type"] = "Fan Merge&Sync",
            ["ACTION"] = "Hijack"
        };

        var url = LConnectClientService.BuildServiceUrl(11021, "LWireless", query);

        Assert.Equal("http://127.0.0.1:11021/?action=LWireless&type=Fan%20Merge%26Sync", url);
    }

    [Fact]
    public void BuildServiceUrl_without_query_is_action_only() =>
        Assert.Equal("http://127.0.0.1:11021/?action=Ping", LConnectClientService.BuildServiceUrl(11021, "Ping", null));

    [Fact]
    public async Task Service_request_with_query_hits_the_default_port()
    {
        var handler = new RecordingHandler(_ => Respond("{\"ok\":true}"));
        using var client = new HttpClient(handler);
        var service = new LConnectClientService();

        var result = await service.SendServiceRequestForJsonAsync(
            client,
            "GetThing",
            new Dictionary<string, string> { ["type"] = "Probe" },
            "{}",
            TestContext.Current.CancellationToken);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(11021, result.Port);
        Assert.Equal("Legacy", result.RequestMode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://127.0.0.1:11021/?action=GetThing&type=Probe", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Empty_success_on_default_port_is_accepted_without_port_discovery_and_cached()
    {
        var handler = new RecordingHandler(_ => Respond(""));
        using var client = new HttpClient(handler);
        var service = new LConnectClientService();

        var first = await service.SendServiceRequestForJsonAsync(client, "ReloadAssets", "{}", TestContext.Current.CancellationToken);

        Assert.Equal(200, first.StatusCode);
        Assert.Equal("OfficialCompatible", first.RequestMode);
        Assert.Collection(
            handler.Requests,
            legacy => Assert.Equal("{}", Encoding.UTF8.GetString(legacy.Body)),
            official => Assert.Empty(official.Body));
        Assert.All(handler.Requests, r => Assert.Equal(11021, r.Uri.Port));

        handler.Requests.Clear();
        var second = await service.SendServiceRequestForJsonAsync(client, "ReloadAssets", "{}", TestContext.Current.CancellationToken);

        Assert.Equal("OfficialCompatible", second.RequestMode);
        Assert.Empty(Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task Caller_cancellation_reaches_the_transport_and_comes_back_as_an_error_result()
    {
        var handler = new RecordingHandler(_ => Respond("{}"));
        using var client = new HttpClient(handler);
        var service = new LConnectClientService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.SendDeviceRequestForJsonAsync(client, "HID#1", "GetTemplates", "{}", cts.Token);

        Assert.Null(result.StatusCode);
        Assert.Contains("Cancel", result.Error);
    }
}

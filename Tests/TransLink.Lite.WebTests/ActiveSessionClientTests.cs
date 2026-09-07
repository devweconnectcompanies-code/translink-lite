using System.Net;
using System.Text;
using TransLink.Lite.Web.Services;

namespace TransLink.Lite.WebTests;

public sealed class ActiveSessionClientTests
{
    [Fact]
    public async Task Discovery_UsesBearerAuthAndParsesOwnedSessions()
    {
        var handler = new RecordingHandler();
        var client = new ActiveSessionClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test") });

        var sessions = await client.GetActiveAsync("temporary-token", default);

        Assert.Single(sessions);
        Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.Equal("temporary-token", handler.Request?.Headers.Authorization?.Parameter);
        Assert.Equal("/api/realtime/sessions/active", handler.Request?.RequestUri?.AbsolutePath);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var id = Guid.NewGuid();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"[{{\"sessionId\":\"{id}\",\"sourceLanguage\":\"en-US\",\"targetLanguage\":\"es\",\"startedAt\":\"2026-01-01T00:00:00Z\"}}]", Encoding.UTF8, "application/json"),
            });
        }
    }
}

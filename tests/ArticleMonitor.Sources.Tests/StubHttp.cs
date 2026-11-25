using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ArticleMonitor.Sources.Tests;

/// <summary>Answers requests from registered routes and records what was requested.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<Uri, Func<HttpRequestMessage, HttpResponseMessage>> _routes = [];
    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();
    private HttpStatusCode _unmatchedStatus = HttpStatusCode.NotFound;

    public IReadOnlyList<HttpRequestMessage> Requests => [.. _requests];

    public StubHttpHandler Respond(Uri url, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _routes[url] = responder;
        return this;
    }

    public StubHttpHandler RespondWithContent(Uri url, string content, string mediaType, string? etag = null) =>
        Respond(url, _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType),
            };
            if (etag is not null) response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
            return response;
        });

    public StubHttpHandler RespondWithStatus(Uri url, HttpStatusCode status) =>
        Respond(url, _ => new HttpResponseMessage(status));

    public StubHttpHandler RespondToUnmatchedWith(HttpStatusCode status)
    {
        _unmatchedStatus = status;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        var url = request.RequestUri ?? throw new InvalidOperationException("Request without URI.");
        var response = _routes.TryGetValue(url, out var responder)
            ? responder(request)
            : new HttpResponseMessage(_unmatchedStatus);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

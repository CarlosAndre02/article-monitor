namespace ArticleMonitor.Infrastructure.Http;

/// <summary>
/// Refuses plain HTTP: content read over it can be altered by anyone on the path and would be stored as the
/// source's. Placed inside <see cref="RedirectHandler"/>, it also checks every redirect target.
/// </summary>
internal sealed class HttpsOnlyHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri ?? throw new InvalidOperationException("Request without URI.");
        if (url.Scheme != Uri.UriSchemeHttps) throw new HttpRequestException($"Only HTTPS is allowed: {url}.");

        return base.SendAsync(request, cancellationToken);
    }
}

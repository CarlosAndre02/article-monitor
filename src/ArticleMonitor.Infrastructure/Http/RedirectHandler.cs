using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace ArticleMonitor.Infrastructure.Http;

/// <summary>
/// Follows redirects itself, outside the other handlers, so each hop goes through the whole pipeline again:
/// HTTPS, robots.txt and rate limit apply to the target as they did to the first URL.
/// The network handler must not follow redirects, or the hops would skip all of that.
/// </summary>
internal sealed class RedirectHandler : DelegatingHandler
{
    // RFC 9309 asks crawlers to follow at least five redirects for robots.txt; it is enough for pages too.
    public const int MaxRedirects = 5;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        for (var redirects = 0; TryGetTarget(response, out var target); redirects++)
        {
            response.Dispose();
            if (redirects == MaxRedirects)
            {
                throw new HttpRequestException($"More than {MaxRedirects} redirects from {request.RequestUri}.");
            }

            response = await base.SendAsync(NextHop(request, target), cancellationToken);
        }

        return response;
    }

    private static bool TryGetTarget(HttpResponseMessage response, [NotNullWhen(true)] out Uri? target)
    {
        target = null;
        if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location) return false;

        var origin = response.RequestMessage?.RequestUri
            ?? throw new InvalidOperationException("Redirect response without its request.");
        target = location.IsAbsoluteUri ? location : new Uri(origin, location);
        return true;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    // The collector only sends GETs, which every redirect status keeps as GET.
    private static HttpRequestMessage NextHop(HttpRequestMessage original, Uri target)
    {
        var next = new HttpRequestMessage(HttpMethod.Get, target) { Version = original.Version };
        foreach (var header in original.Headers) next.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return next;
    }
}

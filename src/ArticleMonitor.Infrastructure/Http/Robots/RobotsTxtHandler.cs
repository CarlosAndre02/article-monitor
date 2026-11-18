namespace ArticleMonitor.Infrastructure.Http.Robots;

public sealed class RobotsDisallowedException(Uri url)
    : HttpRequestException($"robots.txt disallows {url}.")
{
    public Uri Url { get; } = url;
}

/// <summary>Refuses requests that the host's robots.txt disallows, before any network call.</summary>
internal sealed class RobotsTxtHandler(RobotsTxtCache cache) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri ?? throw new InvalidOperationException("Request without URI.");
        var policy = await cache.GetPolicyAsync(url, cancellationToken);
        if (!policy.IsAllowed(url.PathAndQuery)) throw new RobotsDisallowedException(url);

        return await base.SendAsync(request, cancellationToken);
    }
}

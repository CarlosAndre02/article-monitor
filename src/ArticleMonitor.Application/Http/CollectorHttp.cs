namespace ArticleMonitor.Application.Http;

public static class CollectorHttp
{
    /// <summary>
    /// Name of the shared HTTP pipeline (robots.txt, per-host rate limit, resilience, size limit).
    /// Every request to a news source must go through it.
    /// </summary>
    public const string ClientName = "collector";
}

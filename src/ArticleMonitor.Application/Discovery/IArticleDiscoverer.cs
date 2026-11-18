namespace ArticleMonitor.Application.Discovery;

/// <summary>Lists a source's articles.</summary>
/// <remarks>
/// Contract (verified by the shared contract tests):
/// <list type="bullet">
/// <item>Yields articles newest first, across all of the source's listings.</item>
/// <item>Fetches pages lazily: when the consumer stops enumerating, no further requests are made.
/// The core decides when to stop (see <c>DiscoveryStopPolicy</c>).</item>
/// <item>Reports request failures through <see cref="DiscoveryContext.ReportFailure"/> instead of throwing.</item>
/// <item>Uses the shared HTTP pipeline (<c>CollectorHttp.ClientName</c>).</item>
/// </list>
/// </remarks>
public interface IArticleDiscoverer
{
    IAsyncEnumerable<DiscoveredArticle> DiscoverAsync(DiscoveryContext context, CancellationToken cancellationToken);
}

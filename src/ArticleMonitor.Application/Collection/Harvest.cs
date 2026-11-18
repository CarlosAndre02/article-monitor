using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;

namespace ArticleMonitor.Application.Collection;

/// <summary>Everything a single run of a source works on.</summary>
public sealed record HarvestScope(Source Source, ISourceModule Module, CollectionRun Run);

/// <summary>A listed article accepted for collection: valid URL on the source host and a title.</summary>
public sealed record HarvestCandidate(CanonicalUrl Url, ArticleHeadline Headline);

public sealed record HarvestResult(DiscoveryOutcome Outcome, DateTimeOffset? NewestPublishedAt, string? DiscoveryState);

/// <summary>Tracks what one run has already seen.</summary>
internal sealed class HarvestProgress(DateTimeOffset now)
{
    private readonly HashSet<UrlHash> _seen = [];

    public DateTimeOffset? NewestPublishedAt { get; private set; }

    /// <summary>False when the article was already listed in this run (e.g. by another feed of the source).</summary>
    public bool MarkSeen(UrlHash hash) => _seen.Add(hash);

    public void Observe(DateTimeOffset? publishedAt)
    {
        // Future dates (wrong timestamps at the source) would push the watermark ahead and hide real articles.
        if (publishedAt is not { } published || published > now) return;
        if (NewestPublishedAt is null || published > NewestPublishedAt) NewestPublishedAt = published;
    }
}

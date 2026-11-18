using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;
using Microsoft.Extensions.Options;

namespace ArticleMonitor.Application.Collection;

/// <summary>Walks a source's listing newest first, skipping what is known and ingesting what is new.</summary>
public sealed class SourceHarvester(
    IArticleRepository articles,
    ArticleIngestor ingestor,
    IOptions<CollectionOptions> options,
    TimeProvider clock)
{
    // Known-article lookups are batched to avoid one database round trip per listed article.
    private const int LookupBatchSize = 25;

    private enum ItemOutcome
    {
        New,
        Duplicate,
        Skipped,
        Stop,
    }

    public async Task<HarvestResult> HarvestAsync(HarvestScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var now = clock.GetUtcNow();
        var context = new DiscoveryContext(scope.Source.DiscoveryState);
        var policy = DiscoveryStopPolicy.For(scope.Source, options.Value, now);
        var progress = new HarvestProgress(now);

        var listing = scope.Module.Discoverer.DiscoverAsync(context, cancellationToken).Chunk(LookupBatchSize);
        await foreach (var batch in listing.WithCancellation(cancellationToken))
        {
            var keepGoing = await HarvestBatchAsync(batch, scope, policy, progress, cancellationToken);
            if (!keepGoing) break;
        }

        foreach (var failure in context.Failures) scope.Run.RecordFailure(failure);

        var outcome = policy.Complete();

        // After a capped run the old state makes the next run list the same, not yet collected, items again.
        var state = outcome.StopReason == StopReason.ReachedItemCap ? scope.Source.DiscoveryState : context.NextState;
        return new HarvestResult(outcome, progress.NewestPublishedAt, state);
    }

    private async Task<bool> HarvestBatchAsync(
        IReadOnlyList<DiscoveredArticle> batch,
        HarvestScope scope,
        DiscoveryStopPolicy policy,
        HarvestProgress progress,
        CancellationToken cancellationToken)
    {
        var candidates = batch.Select(item => ToCandidate(item, scope.Module.Definition)).ToList();
        var hashes = candidates.OfType<HarvestCandidate>().Select(candidate => candidate.Url.Hash).Distinct();
        var known = await articles.FindExistingAsync([.. hashes], cancellationToken);

        foreach (var candidate in candidates)
        {
            var outcome = Classify(candidate, known, policy, progress);
            if (outcome == ItemOutcome.Stop) return false;

            scope.Run.RecordDiscovered();
            if (outcome == ItemOutcome.Skipped) scope.Run.RecordSkipped();
            if (outcome == ItemOutcome.Duplicate) scope.Run.RecordDuplicate();
            if (outcome == ItemOutcome.New) await ingestor.IngestAsync(candidate!, scope, cancellationToken);
        }

        return true;
    }

    private static ItemOutcome Classify(
        HarvestCandidate? candidate, IReadOnlySet<UrlHash> known, DiscoveryStopPolicy policy, HarvestProgress progress)
    {
        if (candidate is null) return ItemOutcome.Skipped;

        // Listed again by another feed of the same source in this run.
        if (!progress.MarkSeen(candidate.Url.Hash)) return ItemOutcome.Duplicate;

        var decision = policy.Evaluate(candidate.Headline.PublishedAt, known.Contains(candidate.Url.Hash));
        if (decision == DiscoveryDecision.Stop) return ItemOutcome.Stop;

        progress.Observe(candidate.Headline.PublishedAt);
        return decision == DiscoveryDecision.SkipKnown ? ItemOutcome.Duplicate : ItemOutcome.New;
    }

    /// <summary>Null when the item has no valid URL on the source hosts or no title.</summary>
    private static HarvestCandidate? ToCandidate(DiscoveredArticle item, SourceDefinition definition)
    {
        if (!CanonicalUrl.TryCreate(item.Url, out var url) || !url.IsOnAnyHost(definition.AllowedHosts)) return null;
        if (!ArticleHeadline.TryCreate(item.Title, item.Lead, item.PublishedAt, out var headline)) return null;

        return new HarvestCandidate(url, headline);
    }
}

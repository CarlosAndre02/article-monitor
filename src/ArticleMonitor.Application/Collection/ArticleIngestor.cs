using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Application.Pages;
using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Articles;

namespace ArticleMonitor.Application.Collection;

/// <summary>Reads a new article's page, extracts its text and stores it.</summary>
public sealed class ArticleIngestor(
    IPageFetcher pages,
    ArticleContentReader contentReader,
    IArticleRepository articles,
    TimeProvider clock)
{
    public async Task IngestAsync(HarvestCandidate candidate, HarvestScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(scope);

        var definition = scope.Module.Definition;
        var extraction = await ReadContentAsync(candidate, scope, cancellationToken);
        var url = ResolveUrl(candidate.Url, extraction?.CanonicalUrl, definition);
        var article = Article.Collect(
            scope.Source.Id, url, candidate.Headline, definition.Language, extraction?.Text, clock.GetUtcNow());

        var result = await articles.TryAddAsync(article, cancellationToken);
        if (result == AddArticleResult.Added)
        {
            scope.Run.RecordAdded(article.ContentStatus);
            return;
        }

        scope.Run.RecordDuplicate();
    }

    /// <summary>Returns the extracted content, or null when the article is stored with its lead only.</summary>
    private async Task<ExtractionResult?> ReadContentAsync(
        HarvestCandidate candidate, HarvestScope scope, CancellationToken cancellationToken)
    {
        var pageUrl = candidate.Url.ToUri();
        var page = await pages.FetchAsync(pageUrl, cancellationToken);
        if (page.Status == PageFetchStatus.BlockedByRobots)
        {
            scope.Run.RecordBlockedByRobots();
            return null;
        }

        if (!page.IsFetched)
        {
            scope.Run.RecordFailure($"{candidate.Url}: {page.Error}");
            return null;
        }

        var reading = contentReader.Read(scope.Module.Extractor, page.Html, pageUrl);
        if (reading.UsedFallback) scope.Run.RecordExtractionFallback();

        return reading.Result.IsSuccess ? reading.Result : null;
    }

    // The page's canonical URL wins only when it stays on the source; otherwise the listed URL is kept.
    private static CanonicalUrl ResolveUrl(CanonicalUrl listed, string? declared, SourceDefinition definition) =>
        CanonicalUrl.TryCreate(declared, out var canonical) && canonical.IsOnAnyHost(definition.AllowedHosts)
            ? canonical
            : listed;
}

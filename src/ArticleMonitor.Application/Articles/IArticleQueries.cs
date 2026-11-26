using ArticleMonitor.Domain.Articles;

namespace ArticleMonitor.Application.Articles;

/// <summary>Read side of the stored articles, for whoever consults them rather than collects them.</summary>
public interface IArticleQueries
{
    /// <summary>The <paramref name="count"/> articles stored most recently, newest first.</summary>
    Task<IReadOnlyList<CollectedArticle>> ListLatestAsync(int count, CancellationToken cancellationToken);
}

public sealed record CollectedArticle(
    long Id,
    string Source,
    string Url,
    string Title,
    string? Lead,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CollectedAt,
    ContentStatus ContentStatus,
    string? Content);

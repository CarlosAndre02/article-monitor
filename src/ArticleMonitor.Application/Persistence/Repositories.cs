using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;

namespace ArticleMonitor.Application.Persistence;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISourceRepository
{
    Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken);

    Task<Source?> FindAsync(int id, CancellationToken cancellationToken);

    void Add(Source source);
}

public enum AddArticleResult
{
    Added,
    Duplicate,
}

public interface IArticleRepository
{
    Task<IReadOnlySet<UrlHash>> FindExistingAsync(
        IReadOnlyCollection<UrlHash> hashes, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the article right away; the unique URL hash turns a concurrent duplicate into a result.
    /// </summary>
    Task<AddArticleResult> TryAddAsync(Article article, CancellationToken cancellationToken);
}

public interface ICollectionRunRepository
{
    void Add(CollectionRun run);

    Task<IReadOnlyList<CollectionRun>> ListRunningAsync(CancellationToken cancellationToken);
}

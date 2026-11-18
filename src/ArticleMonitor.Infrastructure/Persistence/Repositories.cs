using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ArticleMonitor.Infrastructure.Persistence;

internal sealed class SourceRepository(ArticleMonitorDbContext db) : ISourceRepository
{
    public async Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken) =>
        await db.Sources.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<Source?> FindAsync(int id, CancellationToken cancellationToken) =>
        await db.Sources.FindAsync([id], cancellationToken);

    public void Add(Source source) => db.Sources.Add(source);
}

internal sealed class CollectionRunRepository(ArticleMonitorDbContext db) : ICollectionRunRepository
{
    public void Add(CollectionRun run) => db.CollectionRuns.Add(run);

    public async Task<IReadOnlyList<CollectionRun>> ListRunningAsync(CancellationToken cancellationToken) =>
        await db.CollectionRuns.Where(run => run.Status == RunStatus.Running).ToListAsync(cancellationToken);
}

internal sealed class ArticleRepository(ArticleMonitorDbContext db) : IArticleRepository
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<IReadOnlySet<UrlHash>> FindExistingAsync(
        IReadOnlyCollection<UrlHash> hashes, CancellationToken cancellationToken)
    {
        if (hashes.Count == 0) return new HashSet<UrlHash>();

        var existing = await db.Articles.AsNoTracking()
            .Where(article => hashes.Contains(article.UrlHash))
            .Select(article => article.UrlHash)
            .ToListAsync(cancellationToken);

        return existing.ToHashSet();
    }

    public async Task<AddArticleResult> TryAddAsync(Article article, CancellationToken cancellationToken)
    {
        db.Articles.Add(article);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AddArticleResult.Added;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Other pending changes (e.g. run counters) stay tracked and are saved on the next call.
            db.Entry(article).State = EntityState.Detached;
            return AddArticleResult.Duplicate;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation };
}

using ArticleMonitor.Application.Articles;
using Microsoft.EntityFrameworkCore;

namespace ArticleMonitor.Infrastructure.Persistence;

internal sealed class ArticleQueries(ArticleMonitorDbContext db) : IArticleQueries
{
    public async Task<IReadOnlyList<CollectedArticle>> ListLatestAsync(int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        // The identity grows with each insert, and the clustered key makes this read only the rows returned.
        var rows = await db.Articles.AsNoTracking()
            .OrderByDescending(article => article.Id)
            .Take(count)
            .Join(db.Sources, article => article.SourceId, source => source.Id, (article, source) => new
            {
                Article = article,
                source.Key,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .OrderByDescending(row => row.Article.Id)
                .Select(row => new CollectedArticle(
                    row.Article.Id,
                    row.Key.Value,
                    row.Article.Url,
                    row.Article.Title,
                    row.Article.Lead,
                    row.Article.PublishedAt,
                    row.Article.CollectedAt,
                    row.Article.ContentStatus,
                    row.Article.Content)),
        ];
    }
}

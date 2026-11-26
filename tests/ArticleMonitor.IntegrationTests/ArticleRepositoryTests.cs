using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Sources;
using ArticleMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArticleMonitor.IntegrationTests;

public class ArticleRepositoryTests(SqlServerFixture sql)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_only_articles_already_stored()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        var sourceId = await RegisterSourceAsync(app);
        await app.WithDbAsync(db => new ArticleRepository(db).TryAddAsync(NewArticle(sourceId, "a"), Ct));
        await app.WithDbAsync(db => new ArticleRepository(db).TryAddAsync(NewArticle(sourceId, "b"), Ct));

        var hashes = new[] { "a", "b", "c" }.Select(slug => Url(slug).Hash).ToList();
        var existing = await app.WithDbAsync(db => new ArticleRepository(db).FindExistingAsync(hashes, Ct));

        Assert.Equal(new HashSet<UrlHash> { Url("a").Hash, Url("b").Hash }, existing);
    }

    [Fact]
    public async Task Concurrent_inserts_of_the_same_article_store_it_once()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        var sourceId = await RegisterSourceAsync(app);

        var results = await Task.WhenAll(
            app.WithDbAsync(db => new ArticleRepository(db).TryAddAsync(NewArticle(sourceId, "a"), Ct)),
            app.WithDbAsync(db => new ArticleRepository(db).TryAddAsync(NewArticle(sourceId, "a"), Ct)));

        Assert.Equal([AddArticleResult.Added, AddArticleResult.Duplicate], results.Order());
        Assert.Equal(1, await app.WithDbAsync(db => db.Articles.CountAsync(Ct)));
    }

    [Fact]
    public async Task Article_text_survives_compression()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        var sourceId = await RegisterSourceAsync(app);
        const string text = "Parágrafo com acentuação, “aspas” e emoji 🗒️.\n\nSegundo parágrafo.";

        await app.WithDbAsync(db => new ArticleRepository(db).TryAddAsync(NewArticle(sourceId, "a", text), Ct));

        Assert.Equal(text, await app.WithDbAsync(db => db.Articles.Select(article => article.Content).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Lists_the_latest_articles_newest_first_with_their_text()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        var sourceId = await RegisterSourceAsync(app);
        foreach (var slug in new[] { "a", "b", "c" })
        {
            var article = NewArticle(sourceId, slug, $"Texto {slug}.");
            await app.WithDbAsync(db => new ArticleRepository(db).TryAddAsync(article, Ct));
        }

        var latest = await app.WithDbAsync(db => new ArticleQueries(db).ListLatestAsync(2, Ct));

        Assert.Equal(["Título c", "Título b"], latest.Select(article => article.Title));
        Assert.Equal(("fake", "Texto c."), (latest[0].Source, latest[0].Content));
    }

    private static async Task<int> RegisterSourceAsync(TestApplication app) =>
        await app.WithDbAsync(async (ArticleMonitorDbContext db) =>
        {
            var source = Source.Register(SourceKey.From("fake"));
            db.Sources.Add(source);
            await db.SaveChangesAsync(Ct);
            return source.Id;
        });

    private static Article NewArticle(int sourceId, string slug, string content = "Texto.")
    {
        ArticleHeadline.TryCreate($"Título {slug}", lead: null, publishedAt: null, out var headline);
        return Article.Collect(sourceId, Url(slug), headline!, "pt-BR", content, TestApplication.Start);
    }

    private static CanonicalUrl Url(string slug)
    {
        CanonicalUrl.TryCreate($"https://news.example/{slug}", out var url);
        return url!;
    }
}

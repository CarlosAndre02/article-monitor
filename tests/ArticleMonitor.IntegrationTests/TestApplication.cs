using System.Runtime.CompilerServices;
using ArticleMonitor.Application;
using ArticleMonitor.Application.Collection;
using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Application.Pages;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Sources;
using ArticleMonitor.Infrastructure;
using ArticleMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArticleMonitor.IntegrationTests;

/// <summary>
/// The real application wiring (use cases, repositories, SQL Server) with a fake source module and fake pages,
/// on a fresh database.
/// </summary>
internal sealed class TestApplication : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 30, 0, TimeSpan.FromHours(-3));

    private readonly ServiceProvider _services;

    private TestApplication(ServiceProvider services, FakeTimeProvider clock, FakeModule module)
    {
        _services = services;
        Clock = clock;
        Module = module;
    }

    public FakeTimeProvider Clock { get; }

    public FakeModule Module { get; }

    public ICollectionCycle Cycle => _services.GetRequiredService<ICollectionCycle>();

    public static async Task<TestApplication> StartAsync(SqlServerFixture sql, CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ArticleMonitor"] = sql.ConnectionStringForNewDatabase(),
                ["Collector:Schedule:Times:0"] = "08:00",
                ["Collector:Schedule:Times:1"] = "16:00",
                ["Collector:Schedule:TimeZone"] = "America/Sao_Paulo",
                ["Collector:Http:UserAgent"] = "article-monitor-tests/1.0",
            })
            .Build();

        var clock = new FakeTimeProvider(Start);
        var module = new FakeModule();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddSingleton<TimeProvider>(clock)
            .AddApplication()
            .AddInfrastructure(configuration)
            .AddSingleton<ISourceModule>(module)
            .AddSingleton<IPageFetcher, FakePages>()
            .AddSingleton<IFallbackContentExtractor, NoFallback>()
            .BuildServiceProvider(validateScopes: true);

        var app = new TestApplication(services, clock, module);
        await app.WithDbAsync(db => db.Database.MigrateAsync(cancellationToken));
        return app;
    }

    public async Task WithDbAsync(Func<ArticleMonitorDbContext, Task> action)
    {
        await using var scope = _services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ArticleMonitorDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<ArticleMonitorDbContext, Task<T>> query)
    {
        await using var scope = _services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ArticleMonitorDbContext>());
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

internal sealed class FakeModule : ISourceModule, IArticleDiscoverer, IArticleContentExtractor
{
    public static readonly SourceKey Key = SourceKey.From("fake");

    public SourceDefinition Definition { get; } = new(Key, "Fake", "pt-BR", ["news.example"]);

    public IArticleDiscoverer Discoverer => this;

    public IArticleContentExtractor Extractor => this;

    public IReadOnlyList<DiscoveredArticle> Listing { get; set; } = [];

    public async IAsyncEnumerable<DiscoveredArticle> DiscoverAsync(
        DiscoveryContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        context.SaveState("{\"cursor\":1}");
        foreach (var article in Listing)
        {
            await Task.Yield();
            yield return article;
        }
    }

    public ExtractionResult Extract(string html, Uri pageUrl) => ExtractionResult.Succeeded(html);
}

internal sealed class FakePages : IPageFetcher
{
    public Task<PageFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken) =>
        Task.FromResult(PageFetchResult.Fetched(ArticleText(url)));

    public static string ArticleText(Uri url) =>
        $"Texto completo da matéria {url.AbsolutePath}. " + new string('x', 250);
}

internal sealed class NoFallback : IFallbackContentExtractor
{
    public ExtractionResult Extract(string html, Uri pageUrl) => ExtractionResult.Failed("No fallback in tests.");
}

using System.Runtime.CompilerServices;
using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Application.Pages;
using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace ArticleMonitor.UnitTests.Application;

internal sealed class FakeSourceModule(IArticleDiscoverer discoverer, IArticleContentExtractor extractor)
    : ISourceModule
{
    public SourceDefinition Definition { get; } = new(SourceKey.From("fake"), "Fake", "pt-BR", ["news.example"]);

    public IArticleDiscoverer Discoverer { get; } = discoverer;

    public IArticleContentExtractor Extractor { get; } = extractor;
}

/// <summary>Lists fixed articles, reports fixed failures and saves a fixed state.</summary>
internal sealed class ListDiscoverer(
    IReadOnlyList<DiscoveredArticle> articles,
    IReadOnlyList<string>? failures = null,
    string? nextState = "next-state") : IArticleDiscoverer
{
    public int Yielded { get; private set; }

    public async IAsyncEnumerable<DiscoveredArticle> DiscoverAsync(
        DiscoveryContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var failure in failures ?? []) context.ReportFailure(failure);
        context.SaveState(nextState);

        foreach (var article in articles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            Yielded++;
            yield return article;
        }
    }
}

internal sealed class ThrowingDiscoverer : IArticleDiscoverer
{
    public IAsyncEnumerable<DiscoveredArticle> DiscoverAsync(
        DiscoveryContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Bug.");
}

/// <summary>Extracts "text:..." pages, fails on anything else.</summary>
internal sealed class PrefixExtractor(string prefix = "text:", string? canonicalUrl = null) : IFallbackContentExtractor
{
    public ExtractionResult Extract(string html, Uri pageUrl) =>
        html.StartsWith(prefix, StringComparison.Ordinal)
            ? ExtractionResult.Succeeded(html[prefix.Length..], canonicalUrl)
            : ExtractionResult.Failed("Not an article.");
}

internal sealed class ThrowingExtractor : IArticleContentExtractor
{
    public ExtractionResult Extract(string html, Uri pageUrl) => throw new InvalidOperationException("Bug.");
}

internal sealed class FakePageFetcher : IPageFetcher
{
    private readonly Dictionary<Uri, PageFetchResult> _pages = [];

    public List<Uri> Requested { get; } = [];

    public Action<Uri>? OnFetch { get; set; }

    public FakePageFetcher Serve(string url, PageFetchResult result)
    {
        _pages[new Uri(url)] = result;
        return this;
    }

    public Task<PageFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        Requested.Add(url);
        OnFetch?.Invoke(url);
        cancellationToken.ThrowIfCancellationRequested();

        var page = _pages.GetValueOrDefault(url) ?? PageFetchResult.Fetched("text:" + new string('x', 300));
        return Task.FromResult(page);
    }
}

internal sealed class InMemoryArticleRepository : IArticleRepository
{
    private readonly Dictionary<UrlHash, Article> _articles = [];

    public IReadOnlyCollection<Article> Articles => _articles.Values;

    public void Seed(params string[] urls)
    {
        foreach (var url in urls)
        {
            CanonicalUrl.TryCreate(url, out var canonical);
            ArticleHeadline.TryCreate("Known", lead: null, publishedAt: null, out var headline);
            var article = Article.Collect(1, canonical!, headline!, "pt-BR", "text", DateTimeOffset.UnixEpoch);
            _articles[article.UrlHash] = article;
        }
    }

    public Task<IReadOnlySet<UrlHash>> FindExistingAsync(
        IReadOnlyCollection<UrlHash> hashes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<UrlHash>>(hashes.Where(_articles.ContainsKey).ToHashSet());

    public Task<AddArticleResult> TryAddAsync(Article article, CancellationToken cancellationToken)
    {
        var result = _articles.TryAdd(article.UrlHash, article) ? AddArticleResult.Added : AddArticleResult.Duplicate;
        return Task.FromResult(result);
    }
}

internal sealed class InMemoryCollectionRunRepository : ICollectionRunRepository
{
    public List<CollectionRun> Runs { get; } = [];

    public void Add(CollectionRun run) => Runs.Add(run);

    public Task<IReadOnlyList<CollectionRun>> ListRunningAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectionRun>>([.. Runs.Where(run => run.Status == RunStatus.Running)]);
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public List<object> Scopes { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        Scopes.Add(state);
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

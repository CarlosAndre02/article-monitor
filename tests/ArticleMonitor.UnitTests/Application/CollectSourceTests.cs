using ArticleMonitor.Application.Collection;
using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Application.Pages;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArticleMonitor.UnitTests.Application;

public class CollectSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryArticleRepository _articles = new();
    private readonly InMemoryCollectionRunRepository _runs = new();
    private readonly FakePageFetcher _pages = new();
    private readonly CollectionOptions _options = new() { KnownStreakToStop = 3 };
    private IFallbackContentExtractor _fallback = new PrefixExtractor("fallback:");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Collects_new_articles_and_completes_the_run()
    {
        var source = NewSource();
        var module = Module([Listed("a", Now.AddHours(-1)), Listed("b", Now.AddHours(-2))]);

        var run = await CreateUseCase().ExecuteAsync(source, module, RunTrigger.CatchUp, Ct);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(StopReason.EndOfSource, run.StopReason);
        Assert.False(run.PossibleGap);
        Assert.Equal(2, run.Added);
        Assert.All(_articles.Articles, article => Assert.Equal(ContentStatus.Full, article.ContentStatus));
        Assert.Equal(Now.AddHours(-1), source.Watermark);
        Assert.Equal(Now, source.LastCompletedRunStartedAt);
        Assert.Equal("next-state", source.DiscoveryState);
    }

    [Fact]
    public async Task Counts_an_article_listed_by_two_feeds_once()
    {
        var module = Module([Listed("a?utm_source=politica", Now), Listed("a?utm_source=economia", Now)]);

        var run = await CreateUseCase().ExecuteAsync(NewSource(), module, RunTrigger.Scheduled, Ct);

        Assert.Equal(1, run.Added);
        Assert.Equal(1, run.Duplicates);
        Assert.Single(_pages.Requested);
    }

    [Fact]
    public async Task Skips_links_outside_the_source_and_items_without_title()
    {
        var module = Module([Listed("a", Now, host: "other.example"), Listed("b", Now, title: "  ")]);

        var run = await CreateUseCase().ExecuteAsync(NewSource(), module, RunTrigger.Scheduled, Ct);

        Assert.Equal(2, run.Skipped);
        Assert.Equal(0, run.Added);
        Assert.Empty(_pages.Requested);
    }

    [Fact]
    public async Task Stops_at_known_articles_without_fetching_them()
    {
        _articles.Seed(Url("b"), Url("c"), Url("d"));
        var module = Module(
        [
            Listed("a", Now.AddHours(-1)), Listed("b", Now.AddHours(-2)), Listed("c", Now.AddHours(-3)),
            Listed("d", Now.AddHours(-4)), Listed("e", Now.AddHours(-5)),
        ]);

        var run = await CreateUseCase().ExecuteAsync(NewSource(), module, RunTrigger.Scheduled, Ct);

        Assert.Equal(StopReason.ReachedKnownItems, run.StopReason);
        Assert.Equal(4, run.Discovered);
        Assert.Equal(1, run.Added);
        Assert.Equal(3, run.Duplicates);
        Assert.Equal([new Uri(Url("a"))], _pages.Requested);
    }

    [Fact]
    public async Task Stores_only_the_lead_when_the_page_fails()
    {
        _pages.Serve(Url("a"), PageFetchResult.Failed("HTTP 500"));

        var run = await CreateUseCase().ExecuteAsync(NewSource(), Module([Listed("a", Now)]), RunTrigger.Scheduled, Ct);

        Assert.Equal(RunStatus.PartiallySucceeded, run.Status);
        Assert.Equal(1, run.LeadOnly);
        Assert.Contains("HTTP 500", run.Errors, StringComparison.Ordinal);
        Assert.Equal(ContentStatus.LeadOnly, Assert.Single(_articles.Articles).ContentStatus);
    }

    [Fact]
    public async Task Stores_only_the_lead_of_pages_blocked_by_robots()
    {
        _pages.Serve(Url("a"), PageFetchResult.Blocked());

        var run = await CreateUseCase().ExecuteAsync(NewSource(), Module([Listed("a", Now)]), RunTrigger.Scheduled, Ct);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(1, run.BlockedByRobots);
        Assert.Equal(1, run.LeadOnly);
    }

    [Fact]
    public async Task Falls_back_to_the_generic_extractor()
    {
        _pages.Serve(Url("a"), PageFetchResult.Fetched("fallback:" + new string('y', 300)));

        var run = await CreateUseCase().ExecuteAsync(NewSource(), Module([Listed("a", Now)]), RunTrigger.Scheduled, Ct);

        Assert.Equal(1, run.ExtractionFallbacks);
        Assert.Equal(new string('y', 300), Assert.Single(_articles.Articles).Content);
    }

    [Fact]
    public async Task A_throwing_extractor_costs_only_its_article()
    {
        _fallback = new PrefixExtractor("text:");
        var module = Module([Listed("a", Now)], extractor: new ThrowingExtractor());

        var run = await CreateUseCase().ExecuteAsync(NewSource(), module, RunTrigger.Scheduled, Ct);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(1, run.ExtractionFallbacks);
        Assert.Equal(ContentStatus.Full, Assert.Single(_articles.Articles).ContentStatus);
    }

    [Fact]
    public async Task Keeps_going_when_one_feed_fails()
    {
        var module = Module([Listed("a", Now)], failures: ["Feed Política: HTTP 503"]);

        var run = await CreateUseCase().ExecuteAsync(NewSource(), module, RunTrigger.Scheduled, Ct);

        Assert.Equal(RunStatus.PartiallySucceeded, run.Status);
        Assert.Equal(1, run.Added);
        Assert.Contains("Feed Política", run.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_run_keeps_watermark_and_discovery_state()
    {
        var source = NewSource(watermark: Now.AddHours(-5), state: "old-state");
        var module = Module([], failures: ["Feed Política: timeout", "Feed Economia: timeout"]);

        var run = await CreateUseCase().ExecuteAsync(source, module, RunTrigger.Scheduled, Ct);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(Now.AddHours(-5), source.Watermark);
        Assert.Equal("old-state", source.DiscoveryState);
        Assert.Equal(Now, source.LastCompletedRunStartedAt);
    }

    [Fact]
    public async Task An_interrupted_run_leaves_the_source_for_the_next_run()
    {
        var source = NewSource(watermark: Now.AddHours(-5), state: "old-state");
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _pages.OnFetch = _ => shutdown.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateUseCase().ExecuteAsync(source, Module([Listed("a", Now)]), RunTrigger.Scheduled, shutdown.Token));

        Assert.Equal(RunStatus.Interrupted, Assert.Single(_runs.Runs).Status);
        Assert.Equal(Now.AddHours(-5), source.Watermark);
        Assert.Equal("old-state", source.DiscoveryState);
        Assert.Equal(Now.AddHours(-5), source.LastCompletedRunStartedAt);
    }

    [Theory]
    [InlineData("https://news.example/canonical", "https://news.example/canonical")]
    [InlineData("https://other.example/canonical", "https://news.example/a")]
    public async Task Uses_the_page_canonical_url_only_when_it_stays_on_the_source(string declared, string expected)
    {
        var module = Module([Listed("a", Now)], extractor: new PrefixExtractor("text:", canonicalUrl: declared));

        await CreateUseCase().ExecuteAsync(NewSource(), module, RunTrigger.Scheduled, Ct);

        Assert.Equal(expected, Assert.Single(_articles.Articles).Url);
    }

    [Fact]
    public async Task Ignores_future_dates_for_the_watermark()
    {
        var source = NewSource();
        var module = Module([Listed("a", Now.AddDays(1)), Listed("b", Now.AddHours(-1))]);

        await CreateUseCase().ExecuteAsync(source, module, RunTrigger.Scheduled, Ct);

        Assert.Equal(Now.AddHours(-1), source.Watermark);
    }

    [Fact]
    public async Task A_capped_run_keeps_the_previous_discovery_state()
    {
        _options.MaxItemsPerRun = 1;
        var source = NewSource(watermark: Now.AddDays(-1), state: "old-state");
        var module = Module([Listed("a", Now.AddHours(-1)), Listed("b", Now.AddHours(-2))]);

        var run = await CreateUseCase().ExecuteAsync(source, module, RunTrigger.Scheduled, Ct);

        Assert.Equal(StopReason.ReachedItemCap, run.StopReason);
        Assert.True(run.PossibleGap);
        Assert.Equal("old-state", source.DiscoveryState);
    }

    private CollectSource CreateUseCase()
    {
        var options = Options.Create(_options);
        var reader = new ArticleContentReader(_fallback, options, NullLogger<ArticleContentReader>.Instance);
        var ingestor = new ArticleIngestor(_pages, reader, _articles, _clock);
        var harvester = new SourceHarvester(_articles, ingestor, options, _clock);
        return new CollectSource(harvester, _runs, new FakeUnitOfWork(), _clock, NullLogger<CollectSource>.Instance);
    }

    private static FakeSourceModule Module(
        IReadOnlyList<DiscoveredArticle> articles,
        IReadOnlyList<string>? failures = null,
        IArticleContentExtractor? extractor = null) =>
        new(new ListDiscoverer(articles, failures), extractor ?? new PrefixExtractor());

    private static Source NewSource(DateTimeOffset? watermark = null, string? state = null)
    {
        var source = Source.Register(SourceKey.From("fake"));
        if (watermark is not null) source.CompleteRun(watermark.Value, watermark, state);
        return source;
    }

    private static DiscoveredArticle Listed(
        string slug, DateTimeOffset? publishedAt, string host = "news.example", string? title = "Título") =>
        new(Url(slug, host), title, "Lead da matéria.", publishedAt);

    private static string Url(string slug, string host = "news.example") => $"https://{host}/{slug}";
}

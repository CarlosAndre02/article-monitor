using ArticleMonitor.Application.Collection;
using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Sources.Common.Rss;
using ArticleMonitor.Sources.G1;

namespace ArticleMonitor.LiveTests;

/// <summary>
/// The G1 module against the real site. Catches what the synthetic fixtures cannot: G1 changing its feeds, its
/// HTML or its robots.txt. Checks structure only, never specific articles, which change every day.
/// </summary>
public sealed class G1LiveTests(LiveSources sources) : IClassFixture<LiveSources>
{
    // Few pages, to keep the tests polite; enough that one odd article does not decide the result.
    private const int ArticlesToRead = 3;

    private static readonly int MinExtractedChars = new CollectionOptions().MinExtractedChars;

    public static TheoryData<string> Feeds => [.. G1SourceModule.Feeds.Select(feed => feed.Label)];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory(Explicit = true)]
    [MemberData(nameof(Feeds))]
    public async Task Each_feed_lists_dated_articles_on_the_source(string label)
    {
        var feed = G1SourceModule.Feeds.Single(feed => feed.Label == label);
        var discoverer = new RssDiscoverer(sources.HttpClientFactory, [feed]);

        var (articles, failures) = await DiscoverAsync(discoverer);

        AssertNone(failures);
        Assert.NotEmpty(articles);
        Assert.All(articles, article =>
        {
            Assert.True(IsHttpsOnTheSource(article.Url), $"Not an HTTPS URL on G1: {article.Url}");
            Assert.False(string.IsNullOrWhiteSpace(article.Title), $"No title: {article.Url}");
            Assert.True(article.PublishedAt is not null, $"No publication date: {article.Url}");
        });
    }

    [Fact(Explicit = true)]
    public async Task The_g1_extractor_reads_the_latest_articles()
    {
        var module = sources.Module(G1SourceModule.SourceDefinition.Key.Value);
        var (articles, failures) = await DiscoverAsync(module.Discoverer);
        AssertNone(failures);

        var problems = new List<string>();
        foreach (var article in articles.Take(ArticlesToRead))
        {
            problems.AddRange(await ReadAsync(module.Extractor, new Uri(article.Url)));
        }

        AssertNone(problems);
    }

    private async Task<IEnumerable<string>> ReadAsync(IArticleContentExtractor extractor, Uri url)
    {
        var page = await sources.Pages.FetchAsync(url, Ct);
        if (!page.IsFetched) return [$"{url}: page {page.Status} ({page.Error})"];

        var result = extractor.Extract(page.Html, url);
        if (!result.IsSuccess) return [$"{url}: extraction failed ({result.FailureReason})"];
        if (result.Text.Length < MinExtractedChars) return [$"{url}: only {result.Text.Length} chars extracted"];
        if (result.CanonicalUrl is { } canonical && !IsHttpsOnTheSource(canonical))
        {
            return [$"{url}: canonical URL off the source ({canonical})"];
        }

        return [];
    }

    private static async Task<(IReadOnlyList<DiscoveredArticle> Articles, IReadOnlyList<string> Failures)>
        DiscoverAsync(IArticleDiscoverer discoverer)
    {
        var context = new DiscoveryContext(previousState: null);
        var articles = await discoverer.DiscoverAsync(context, Ct).ToListAsync(Ct);
        return (articles, context.Failures);
    }

    // Assert.Empty truncates long items, and the URL and reason are what says what changed on the site.
    private static void AssertNone(IReadOnlyCollection<string> problems) =>
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

    private static bool IsHttpsOnTheSource(string raw) =>
        CanonicalUrl.TryCreate(raw, out var url)
        && url.Value.StartsWith("https://", StringComparison.Ordinal)
        && url.IsOnAnyHost(G1SourceModule.SourceDefinition.AllowedHosts);
}

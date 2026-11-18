using System.Net;
using ArticleMonitor.Application.Discovery;
using Xunit;

namespace ArticleMonitor.Sources.Testing;

/// <summary>
/// Rules every <see cref="IArticleDiscoverer"/> must follow. A source module's test project inherits this class
/// and wires its discoverer to its fixtures; the core's stop policy relies on these rules.
/// </summary>
public abstract class ArticleDiscovererContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discovers_articles_with_absolute_http_urls_and_titles()
    {
        var articles = await DiscoverAllAsync(ServedFixtures(), new DiscoveryContext(previousState: null));

        Assert.NotEmpty(articles);
        Assert.All(articles, article =>
        {
            Assert.True(Uri.TryCreate(article.Url, UriKind.Absolute, out var url), article.Url);
            Assert.Contains(url.Scheme, new[] { Uri.UriSchemeHttp, Uri.UriSchemeHttps });
            Assert.False(string.IsNullOrWhiteSpace(article.Title), article.Url);
        });
    }

    [Fact]
    public async Task Yields_articles_newest_first()
    {
        var articles = await DiscoverAllAsync(ServedFixtures(), new DiscoveryContext(previousState: null));
        var dates = articles.Select(article => article.PublishedAt).OfType<DateTimeOffset>().ToList();

        Assert.Equal(dates.OrderByDescending(date => date), dates);
    }

    [Fact]
    public async Task Stops_requesting_once_the_consumer_stops()
    {
        var http = ServedFixtures();
        var discoverer = CreateDiscoverer(new StubHttpClientFactory(http));

        await foreach (var _ in discoverer.DiscoverAsync(new DiscoveryContext(previousState: null), Ct)) break;
        var requestsWhenStopped = http.Requests.Count;
        await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);

        Assert.Equal(requestsWhenStopped, http.Requests.Count);
    }

    [Fact]
    public async Task Reports_failures_instead_of_throwing()
    {
        var http = new StubHttpHandler().RespondToUnmatchedWith(HttpStatusCode.ServiceUnavailable);
        var context = new DiscoveryContext(previousState: null);

        var articles = await DiscoverAllAsync(http, context);

        Assert.Empty(articles);
        Assert.NotEmpty(context.Failures);
    }

    /// <summary>Creates the discoverer under test on top of the given HTTP client factory.</summary>
    protected abstract IArticleDiscoverer CreateDiscoverer(IHttpClientFactory httpClientFactory);

    /// <summary>Registers the module's fixtures for every URL its discoverer requests.</summary>
    protected abstract void ServeFixtures(StubHttpHandler http);

    protected async Task<List<DiscoveredArticle>> DiscoverAllAsync(StubHttpHandler http, DiscoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var articles = new List<DiscoveredArticle>();
        await foreach (var article in CreateDiscoverer(new StubHttpClientFactory(http)).DiscoverAsync(context, Ct))
        {
            articles.Add(article);
        }

        return articles;
    }

    protected StubHttpHandler ServedFixtures()
    {
        var http = new StubHttpHandler();
        ServeFixtures(http);
        return http;
    }
}

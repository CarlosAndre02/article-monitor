using System.Net;
using System.Text;
using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Sources.G1;
using ArticleMonitor.Sources.Tests.Contracts;

namespace ArticleMonitor.Sources.Tests.G1;

public class G1DiscoveryTests : ArticleDiscovererContractTests
{
    private static readonly Uri PoliticsFeed = new("https://g1.globo.com/rss/g1/politica/");

    [Fact]
    public async Task Lists_the_items_of_the_three_section_feeds()
    {
        var articles = await DiscoverAllAsync(ServedFixtures(), new DiscoveryContext(previousState: null));

        // 2 + 3 + 2 items; the technology article also listed by the economy feed is deduplicated by the core.
        Assert.Equal(7, articles.Count);
    }

    [Fact]
    public async Task Reads_dates_with_minus_zero_offset_and_the_lead_from_atom_subtitle()
    {
        var articles = await DiscoverAllAsync(ServedFixtures(), new DiscoveryContext(previousState: null));
        var article = articles.Single(
            a => a.Url.EndsWith("senado-aprova-projeto-ficticio.ghtml", StringComparison.Ordinal));

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 14, 53, 47, TimeSpan.Zero), article.PublishedAt);
        Assert.Equal("Texto fictício segue para sanção e prevê relatórios trimestrais.", article.Lead);
    }

    [Fact]
    public async Task Next_run_only_downloads_feeds_that_changed()
    {
        var first = new DiscoveryContext(previousState: null);
        await DiscoverAllAsync(ServedFixtures(), first);

        var http = new StubHttpHandler();
        foreach (var feed in G1SourceModule.Feeds) http.Respond(feed.Url, AnswerConditionally);
        var second = new DiscoveryContext(first.NextState);
        var articles = await DiscoverAllAsync(http, second);

        Assert.Empty(articles);
        Assert.Empty(second.Failures);
        Assert.Equal(first.NextState, second.NextState);
        Assert.All(http.Requests, request => Assert.NotEmpty(request.Headers.IfNoneMatch));
    }

    [Fact]
    public async Task Keeps_the_validators_of_a_feed_that_failed()
    {
        var first = new DiscoveryContext(previousState: null);
        await DiscoverAllAsync(ServedFixtures(), first);

        var http = ServedFixtures().RespondWithStatus(PoliticsFeed, HttpStatusCode.InternalServerError);
        var second = new DiscoveryContext(first.NextState);
        await DiscoverAllAsync(http, second);

        Assert.Contains("Política", Assert.Single(second.Failures), StringComparison.Ordinal);
        Assert.Contains("politica-v1", second.NextState, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saves_only_the_http_validators_in_the_state()
    {
        var context = new DiscoveryContext(previousState: null);
        await DiscoverAllAsync(ServedFixtures(), context);

        Assert.Contains("\"ETag\"", context.NextState, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEmpty", context.NextState, StringComparison.Ordinal);
    }

    protected override IArticleDiscoverer CreateDiscoverer(IHttpClientFactory httpClientFactory) =>
        new G1SourceModule(httpClientFactory).Discoverer;

    protected override void ServeFixtures(StubHttpHandler http)
    {
        ArgumentNullException.ThrowIfNull(http);
        foreach (var feed in G1SourceModule.Feeds)
        {
            var etag = $"\"{Fixtures.SectionOf(feed.Url)}-v1\"";
            http.RespondWithContent(feed.Url, Fixtures.FeedFor(feed.Url), "application/xml", etag);
        }
    }

    private static HttpResponseMessage AnswerConditionally(HttpRequestMessage request)
    {
        if (request.Headers.IfNoneMatch.Count > 0) return new HttpResponseMessage(HttpStatusCode.NotModified);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixtures.FeedFor(request.RequestUri!), Encoding.UTF8, "application/xml"),
        };
    }
}

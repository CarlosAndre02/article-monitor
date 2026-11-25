using System.Collections.Concurrent;
using System.Net;
using ArticleMonitor.Application.Http;
using ArticleMonitor.Application.Pages;
using ArticleMonitor.Infrastructure;
using ArticleMonitor.Infrastructure.Http;
using ArticleMonitor.Infrastructure.Http.Robots;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace ArticleMonitor.UnitTests.Infrastructure;

/// <summary>The real collector HTTP pipeline, with only the network replaced by <see cref="StubNetwork"/>.</summary>
public sealed class CollectorHttpPipelineTests : IDisposable
{
    private readonly StubNetwork _network = new();
    private readonly ServiceProvider _services;

    public CollectorHttpPipelineTests()
    {
        _network.Serve("https://other.example/robots.txt", HttpStatusCode.OK, "User-agent: *\nDisallow: /private");
        _services = BuildServices(_network);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IPageFetcher Pages => _services.GetRequiredService<IPageFetcher>();

    [Fact]
    public async Task Follows_a_redirect_through_the_whole_pipeline()
    {
        _network.Redirect("https://news.example/old", "/new");
        _network.Serve("https://news.example/new", HttpStatusCode.OK, "página");

        var page = await Pages.FetchAsync(new Uri("https://news.example/old"), Ct);

        Assert.Equal("página", page.Html);
        Assert.Equal(
            ["https://news.example/robots.txt", "https://news.example/old", "https://news.example/new"],
            _network.Requested);
    }

    [Fact]
    public async Task Applies_the_robots_txt_of_the_redirect_target()
    {
        _network.Redirect("https://news.example/a", "https://other.example/private/page");

        var page = await Pages.FetchAsync(new Uri("https://news.example/a"), Ct);

        Assert.Equal(PageFetchStatus.BlockedByRobots, page.Status);
        Assert.DoesNotContain("https://other.example/private/page", _network.Requested);
    }

    [Fact]
    public async Task Refuses_plain_http_before_any_network_access()
    {
        var page = await Pages.FetchAsync(new Uri("http://news.example/a"), Ct);

        Assert.Equal(PageFetchStatus.Failed, page.Status);
        Assert.Contains("Only HTTPS", page.Error, StringComparison.Ordinal);
        Assert.Empty(_network.Requested);
    }

    [Fact]
    public async Task Refuses_a_redirect_to_plain_http()
    {
        _network.Redirect("https://news.example/a", "http://news.example/a");

        var page = await Pages.FetchAsync(new Uri("https://news.example/a"), Ct);

        Assert.Equal(PageFetchStatus.Failed, page.Status);
        Assert.DoesNotContain("http://news.example/a", _network.Requested);
    }

    [Fact]
    public async Task Gives_up_after_too_many_redirects()
    {
        _network.Redirect("https://news.example/loop", "/loop");

        var page = await Pages.FetchAsync(new Uri("https://news.example/loop"), Ct);

        Assert.Equal(PageFetchStatus.Failed, page.Status);
        Assert.Equal(RedirectHandler.MaxRedirects + 1, _network.Requested.Count(url => url.EndsWith("/loop")));
    }

    [Fact]
    public async Task Refuses_an_article_page_above_its_size_limit()
    {
        _network.Serve("https://news.example/huge", HttpStatusCode.OK, new string('x', 4096));

        var page = await Pages.FetchAsync(new Uri("https://news.example/huge"), Ct);

        Assert.Equal(PageFetchStatus.Failed, page.Status);
    }

    [Fact]
    public async Task A_failing_host_does_not_open_the_circuit_of_the_others()
    {
        _network.Serve("https://down.example/a", HttpStatusCode.InternalServerError, string.Empty);
        _network.Serve("https://news.example/a", HttpStatusCode.OK, "página");

        await Pages.FetchAsync(new Uri("https://down.example/a"), Ct);
        var down = await Pages.FetchAsync(new Uri("https://down.example/a"), Ct);
        var healthy = await Pages.FetchAsync(new Uri("https://news.example/a"), Ct);

        Assert.Contains("circuit", down.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(healthy.IsFetched);
    }

    public void Dispose() => _services.Dispose();

    private static ServiceProvider BuildServices(StubNetwork network)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ArticleMonitor"] = "Server=unused",
                ["Collector:Http:UserAgent"] = "article-monitor-tests/1.0",
                ["Collector:Http:RequestsPerSecondPerHost"] = "50",
                ["Collector:Http:MaxArticlePageBytes"] = "2048",
            })
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddInfrastructure(configuration);
        services.AddHttpClient(CollectorHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => network);
        services.AddHttpClient(RobotsTxtCache.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => network);

        // Two failures in a row open a circuit, without waiting between retries.
        services.Configure<HttpStandardResilienceOptions>($"{CollectorHttp.ClientName}-standard", options =>
        {
            options.Retry.MaxRetryAttempts = 1;
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.BackoffType = DelayBackoffType.Constant;
            options.CircuitBreaker.MinimumThroughput = 2;
            options.CircuitBreaker.FailureRatio = 0.5;
        });

        return services.BuildServiceProvider();
    }

    /// <summary>Answers from registered routes (404 otherwise) and records every URL requested.</summary>
    private sealed class StubNetwork : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _routes = new();
        private readonly ConcurrentQueue<string> _requested = new();

        public IReadOnlyList<string> Requested => [.. _requested];

        public void Serve(string url, HttpStatusCode status, string content) =>
            _routes[url] = () => new HttpResponseMessage(status) { Content = new StringContent(content) };

        public void Redirect(string url, string location) =>
            _routes[url] = () => new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) },
            };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            _requested.Enqueue(url);
            var response = _routes.TryGetValue(url, out var respond)
                ? respond()
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}

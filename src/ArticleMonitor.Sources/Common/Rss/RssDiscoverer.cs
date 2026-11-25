using System.Net;
using System.Runtime.CompilerServices;
using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Http;

namespace ArticleMonitor.Sources.Common.Rss;

public sealed record FeedSource(string Label, Uri Url);

/// <summary>
/// Lists the articles of one or more RSS/Atom feeds, newest first.
/// Feeds are small and unpaginated, so all of them are read before yielding: that is what makes a
/// newest-first order across feeds possible.
/// </summary>
public sealed class RssDiscoverer : IArticleDiscoverer
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyList<FeedSource> _feeds;

    public RssDiscoverer(IHttpClientFactory httpClientFactory, IReadOnlyList<FeedSource> feeds)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(feeds);
        if (feeds.Count == 0) throw new ArgumentException("At least one feed is required.", nameof(feeds));

        _httpClientFactory = httpClientFactory;
        _feeds = feeds;
    }

    public async IAsyncEnumerable<DiscoveredArticle> DiscoverAsync(
        DiscoveryContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = RssDiscoveryState.Parse(context.PreviousState);
        var articles = new List<DiscoveredArticle>();
        foreach (var feed in _feeds)
        {
            var read = await ReadAsync(feed, state.For(feed.Url), cancellationToken);
            if (read.Failure is not null)
            {
                context.ReportFailure($"Feed {feed.Label} ({feed.Url}): {read.Failure}");
                continue;
            }

            articles.AddRange(read.Articles);
            if (read.Validators is not null) state.Update(feed.Url, read.Validators);
        }

        context.SaveState(state.Serialize());
        foreach (var article in articles.OrderByDescending(article => article.PublishedAt))
        {
            yield return article;
        }
    }

    private async Task<FeedRead> ReadAsync(
        FeedSource feed, FeedValidators? validators, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(CollectorHttp.ClientName);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
            validators?.ApplyTo(request.Headers);

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified) return FeedRead.NotModified;
            if (!response.IsSuccessStatusCode) return FeedRead.Failed($"HTTP {(int)response.StatusCode}");

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return FeedRead.Fetched(RssFeedParser.Parse(xml), FeedValidators.From(response));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Any failure of one feed (network, robots.txt, malformed XML) must not stop the others.
            return FeedRead.Failed(exception.Message);
        }
    }

    private sealed record FeedRead(
        IReadOnlyList<DiscoveredArticle> Articles, FeedValidators? Validators, string? Failure)
    {
        public static FeedRead NotModified { get; } = new([], Validators: null, Failure: null);

        public static FeedRead Fetched(IReadOnlyList<DiscoveredArticle> articles, FeedValidators validators) =>
            new(articles, validators, Failure: null);

        public static FeedRead Failed(string reason) => new([], Validators: null, reason);
    }
}

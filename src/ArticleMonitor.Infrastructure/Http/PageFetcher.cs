using ArticleMonitor.Application.Http;
using ArticleMonitor.Application.Pages;
using ArticleMonitor.Infrastructure.Http.Robots;
using Microsoft.Extensions.Options;

namespace ArticleMonitor.Infrastructure.Http;

internal sealed class PageFetcher(IHttpClientFactory httpClientFactory, IOptions<CollectorHttpOptions> options)
    : IPageFetcher
{
    public async Task<PageFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(CollectorHttp.ClientName);
        client.MaxResponseContentBufferSize = options.Value.MaxArticlePageBytes;
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return PageFetchResult.Failed($"HTTP {(int)response.StatusCode}");

            return PageFetchResult.Fetched(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (RobotsDisallowedException)
        {
            return PageFetchResult.Blocked();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Network errors, timeouts, open circuit, oversized response: this page fails, the run goes on.
            return PageFetchResult.Failed(exception.Message);
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArticleMonitor.Infrastructure.Http.Robots;

/// <summary>Fetches and caches each host's robots.txt policy.</summary>
internal sealed partial class RobotsTxtCache(
    IHttpClientFactory httpClientFactory,
    IOptions<CollectorHttpOptions> options,
    TimeProvider clock,
    ILogger<RobotsTxtCache> logger) : IDisposable
{
    public const string HttpClientName = "robots";

    // An unreachable robots.txt blocks the host, so it is retried much sooner than a successful fetch expires.
    private static readonly TimeSpan UnreachableCacheDuration = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, CachedPolicy> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _fetchLock = new(1, 1);

    public async Task<RobotsTxtPolicy> GetPolicyAsync(Uri url, CancellationToken cancellationToken)
    {
        var origin = url.GetLeftPart(UriPartial.Authority);
        if (TryGetFresh(origin, out var policy)) return policy;

        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFresh(origin, out policy)) return policy;

            var fetched = await FetchAsync(origin, cancellationToken);
            _cache[origin] = fetched;
            return fetched.Policy;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    public void Dispose() => _fetchLock.Dispose();

    private bool TryGetFresh(string origin, [NotNullWhen(true)] out RobotsTxtPolicy? policy)
    {
        policy = null;
        if (!_cache.TryGetValue(origin, out var cached) || cached.ExpiresAt <= clock.GetUtcNow()) return false;

        policy = cached.Policy;
        return true;
    }

    private async Task<CachedPolicy> FetchAsync(string origin, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(new Uri($"{origin}/robots.txt"), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return Cache(RobotsTxtPolicy.Parse(content, settings.ProductToken), settings.RobotsTxtCacheDuration);
            }

            // RFC 9309: a client error means there are no rules; a server error means "assume all is disallowed".
            if (IsClientError(response.StatusCode))
            {
                return Cache(RobotsTxtPolicy.AllowAll, settings.RobotsTxtCacheDuration);
            }

            LogUnreachable(logger, origin, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, origin, exception.Message);
        }

        return Cache(RobotsTxtPolicy.DisallowAll, UnreachableCacheDuration);
    }

    private CachedPolicy Cache(RobotsTxtPolicy policy, TimeSpan duration) => new(policy, clock.GetUtcNow() + duration);

    private static bool IsClientError(HttpStatusCode status) => (int)status is >= 400 and < 500;

    [LoggerMessage(
        Level = LogLevel.Warning, Message = "robots.txt of {Origin} unreachable ({Reason}); host blocked for now")]
    private static partial void LogUnreachable(ILogger logger, string origin, string reason);

    private sealed record CachedPolicy(RobotsTxtPolicy Policy, DateTimeOffset ExpiresAt);
}

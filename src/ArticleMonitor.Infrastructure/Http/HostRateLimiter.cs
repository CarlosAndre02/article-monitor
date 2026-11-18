using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ArticleMonitor.Infrastructure.Http;

/// <summary>Spaces requests to the same host, whichever source or client sends them.</summary>
internal sealed class HostRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public HostRateLimiter(IOptions<CollectorHttpOptions> options)
    {
        var period = TimeSpan.FromSeconds(1 / options.Value.RequestsPerSecondPerHost);
        _limiter = PartitionedRateLimiter.Create<string, string>(
            host => RateLimitPartition.GetTokenBucketLimiter(host, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 1,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = period,
                QueueLimit = int.MaxValue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }),
            StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask WaitTurnAsync(string host, CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(host, permitCount: 1, cancellationToken);
        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException($"Rate limit queue for {host} rejected the request.");
        }
    }

    public void Dispose() => _limiter.Dispose();
}

internal sealed class HostRateLimitHandler(HostRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? throw new InvalidOperationException("Request without URI.");
        await limiter.WaitTurnAsync(host, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}

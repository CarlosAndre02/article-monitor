using System.Net;
using ArticleMonitor.Application.Http;
using ArticleMonitor.Application.Pages;
using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Infrastructure.Http;
using ArticleMonitor.Infrastructure.Http.Robots;
using ArticleMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ArticleMonitor.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "ArticleMonitor";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set it with user-secrets "
                + $"or the ConnectionStrings__{ConnectionStringName} environment variable.");

        return services
            .AddPersistence(connectionString)
            .AddCollectorHttp();
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ArticleMonitorDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ArticleMonitorDbContext>());
        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<IArticleRepository, ArticleRepository>();
        services.AddScoped<ICollectionRunRepository, CollectionRunRepository>();
        return services;
    }

    private static IServiceCollection AddCollectorHttp(this IServiceCollection services)
    {
        services.AddOptions<CollectorHttpOptions>()
            .BindConfiguration(CollectorHttpOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HostRateLimiter>();
        services.AddSingleton<RobotsTxtCache>();
        services.AddTransient<RobotsTxtHandler>();
        services.AddTransient<HostRateLimitHandler>();

        // Outer to inner: robots.txt is checked once per request, retries happen inside it,
        // and every attempt (retries included) waits for its turn on the host.
        var collector = services.AddHttpClient(CollectorHttp.ClientName).WithCollectorDefaults();
        collector.AddHttpMessageHandler<RobotsTxtHandler>();
        collector.AddStandardResilienceHandler();
        collector.AddHttpMessageHandler<HostRateLimitHandler>();

        var robots = services.AddHttpClient(RobotsTxtCache.HttpClientName).WithCollectorDefaults();
        robots.AddStandardResilienceHandler();
        robots.AddHttpMessageHandler<HostRateLimitHandler>();

        services.AddSingleton<IPageFetcher, PageFetcher>();
        return services;
    }

    private static IHttpClientBuilder WithCollectorDefaults(this IHttpClientBuilder builder) =>
        builder
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<CollectorHttpOptions>>().Value;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                client.MaxResponseContentBufferSize = options.MaxResponseBytes;
            })
            // Some sources (e.g. G1) send gzip even when it is not requested.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
}

using ArticleMonitor.Application.Pages;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Infrastructure;
using ArticleMonitor.Sources.Common;
using ArticleMonitor.Sources.G1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArticleMonitor.LiveTests;

/// <summary>
/// The source modules wired to the real HTTP pipeline (robots.txt, rate limit, HTTPS, public addresses only),
/// as the worker runs them. One instance per test class, so its requests share the per-host rate limit.
/// </summary>
public sealed class LiveSources : IDisposable
{
    private readonly ServiceProvider _services;

    public LiveSources()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Not used: the live tests never touch the database.
                ["ConnectionStrings:ArticleMonitor"] = "Server=unused",
                // Same product token as the worker, so robots.txt applies the rules the worker gets.
                ["Collector:Http:UserAgent"] = "article-monitor/0.1 (live tests)",
            })
            .Build();

        _services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddInfrastructure(configuration)
            .AddSourcesCommon()
            .AddG1Source()
            .BuildServiceProvider();
    }

    public IHttpClientFactory HttpClientFactory => _services.GetRequiredService<IHttpClientFactory>();

    public IPageFetcher Pages => _services.GetRequiredService<IPageFetcher>();

    public ISourceModule Module(string key) =>
        _services.GetServices<ISourceModule>().Single(module => module.Definition.Key.Value == key);

    public void Dispose() => _services.Dispose();
}

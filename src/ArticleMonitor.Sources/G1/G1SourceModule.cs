using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Sources;
using ArticleMonitor.Sources.Common.Rss;

namespace ArticleMonitor.Sources.G1;

/// <summary>G1 (g1.globo.com): politics, economy and technology sections, discovered through their RSS feeds.</summary>
public sealed class G1SourceModule(IHttpClientFactory httpClientFactory) : ISourceModule
{
    public static SourceDefinition SourceDefinition { get; } =
        new(SourceKey.From("g1"), "G1", "pt-BR", ["g1.globo.com"]);

    public static IReadOnlyList<FeedSource> Feeds { get; } =
    [
        new("Política", new Uri("https://g1.globo.com/rss/g1/politica/")),
        new("Economia", new Uri("https://g1.globo.com/rss/g1/economia/")),
        new("Tecnologia", new Uri("https://g1.globo.com/rss/g1/tecnologia/")),
    ];

    public SourceDefinition Definition => SourceDefinition;

    public IArticleDiscoverer Discoverer { get; } = new RssDiscoverer(httpClientFactory, Feeds);

    public IArticleContentExtractor Extractor { get; } = new G1ArticleExtractor();
}

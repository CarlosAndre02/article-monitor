using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Application.Extraction;

namespace ArticleMonitor.Application.Sources;

/// <summary>
/// Extension point for news sources. A module says how to find a source's articles and how to read their text;
/// everything else (scheduling, deduplication, stop rules, persistence, HTTP politeness) is shared by the core.
/// </summary>
public interface ISourceModule
{
    SourceDefinition Definition { get; }

    IArticleDiscoverer Discoverer { get; }

    IArticleContentExtractor Extractor { get; }
}

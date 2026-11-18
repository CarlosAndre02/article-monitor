namespace ArticleMonitor.Application.Extraction;

/// <summary>Reads an article's text from its page HTML.</summary>
/// <remarks>
/// Contract (verified by the shared contract tests): a pure function that returns
/// <see cref="ExtractionResult.Failed"/> for pages it does not recognize, never throwing.
/// </remarks>
public interface IArticleContentExtractor
{
    ExtractionResult Extract(string html, Uri pageUrl);
}

/// <summary>Generic extractor the core tries when a module's own extractor gets no usable text.</summary>
public interface IFallbackContentExtractor : IArticleContentExtractor;

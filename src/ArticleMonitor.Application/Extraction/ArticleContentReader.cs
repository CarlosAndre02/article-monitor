using ArticleMonitor.Application.Collection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArticleMonitor.Application.Extraction;

public sealed record ContentReading(ExtractionResult Result, bool UsedFallback);

/// <summary>Runs a module's extractor and falls back to the generic one when it gets no usable text.</summary>
public sealed partial class ArticleContentReader(
    IFallbackContentExtractor fallback,
    IOptions<CollectionOptions> options,
    ILogger<ArticleContentReader> logger)
{
    public ContentReading Read(IArticleContentExtractor primary, string html, Uri pageUrl)
    {
        ArgumentNullException.ThrowIfNull(primary);

        var primaryResult = SafeExtract(primary, html, pageUrl);
        if (IsUsable(primaryResult)) return new ContentReading(primaryResult, UsedFallback: false);

        var fallbackResult = SafeExtract(fallback, html, pageUrl);
        if (IsUsable(fallbackResult)) return new ContentReading(fallbackResult, UsedFallback: true);

        // A short text from the module is still better than none.
        return new ContentReading(primaryResult, UsedFallback: false);
    }

    private bool IsUsable(ExtractionResult result) =>
        result.IsSuccess && result.Text.Length >= options.Value.MinExtractedChars;

    // An extractor bug must cost one article, not the whole run.
    private ExtractionResult SafeExtract(IArticleContentExtractor extractor, string html, Uri pageUrl)
    {
        try
        {
            return extractor.Extract(html, pageUrl);
        }
        catch (Exception exception)
        {
            LogExtractorFailed(logger, exception, extractor.GetType().Name, pageUrl);
            return ExtractionResult.Failed($"{extractor.GetType().Name} threw {exception.GetType().Name}.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Extractor {Extractor} threw while reading {PageUrl}")]
    private static partial void LogExtractorFailed(ILogger logger, Exception exception, string extractor, Uri pageUrl);
}

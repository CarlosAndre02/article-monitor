using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Sources.Common.Html;
using SmartReader;

namespace ArticleMonitor.Sources.Common.Extraction;

/// <summary>
/// Generic extractor (port of Mozilla Readability), used when a module's extractor gets no usable text.
/// </summary>
public sealed class ReadabilityExtractor : IFallbackContentExtractor
{
    public ExtractionResult Extract(string html, Uri pageUrl)
    {
        ArgumentNullException.ThrowIfNull(pageUrl);
        if (string.IsNullOrWhiteSpace(html)) return ExtractionResult.Failed("Empty page.");

        var article = new Reader(pageUrl.ToString(), html).GetArticle();
        if (!article.IsReadable) return ExtractionResult.Failed("Readability found no article content.");

        var text = HtmlText.JoinParagraphs(article.TextContent.Split('\n'));
        return text.Length == 0
            ? ExtractionResult.Failed("Readability found an empty article.")
            : ExtractionResult.Succeeded(text);
    }
}

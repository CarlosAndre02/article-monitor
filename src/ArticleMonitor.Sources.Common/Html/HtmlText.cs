using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ArticleMonitor.Sources.Common.Html;

/// <summary>Plain-text helpers shared by extractors.</summary>
public static class HtmlText
{
    public const string ParagraphSeparator = "\n\n";

    public static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Joins non-empty paragraphs, each with collapsed whitespace, separated by a blank line.</summary>
    public static string JoinParagraphs(IEnumerable<string> paragraphs) =>
        string.Join(ParagraphSeparator, paragraphs.Select(CollapseWhitespace).Where(paragraph => paragraph.Length > 0));

    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var text = CollapseWhitespace(new HtmlParser().ParseDocument(html).Body?.TextContent ?? string.Empty);
        return text.Length == 0 ? null : text;
    }

    public static string? CanonicalUrl(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.QuerySelector("link[rel='canonical']")?.GetAttribute("href");
    }
}

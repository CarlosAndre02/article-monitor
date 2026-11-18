using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Sources.Common.Html;

namespace ArticleMonitor.Sources.G1;

/// <summary>Reads the text of a G1 article page.</summary>
public sealed class G1ArticleExtractor : IArticleContentExtractor
{
    // Every text block of the body (paragraph, intertitle, list, quote) is a div.content-text. G1 nests <div>,
    // <ul> and <blockquote> inside <p>, which HTML5 parsers move out of it, so whole blocks are read instead of <p>.
    private const string TextBlockSelector = ".mc-article-body .content-text";

    // Headers of related-content sections; the related links themselves are caught as link-only blocks.
    private static readonly HashSet<string> RelatedContentLabels =
        new(StringComparer.OrdinalIgnoreCase) { "Leia também", "Leia mais", "Veja também", "Vídeos", "Assista" };

    // Promotional lines that are not (or only partly) linked, so the link-only rule does not catch them.
    private static readonly string[] PromotionPrefixes =
        ["Leia mais notícias sobre a região", "Tem alguma sugestão de reportagem", "Clique aqui para seguir"];

    public ExtractionResult Extract(string html, Uri pageUrl)
    {
        ArgumentNullException.ThrowIfNull(pageUrl);
        if (string.IsNullOrWhiteSpace(html)) return ExtractionResult.Failed("Empty page.");

        var document = new HtmlParser().ParseDocument(html);
        var paragraphs = document.QuerySelectorAll(TextBlockSelector)
            .Where(block => !IsBoilerplate(block))
            .SelectMany(Paragraphs);
        var text = HtmlText.JoinParagraphs(paragraphs);

        return text.Length == 0
            ? ExtractionResult.Failed("G1 article body not found.")
            : ExtractionResult.Succeeded(text, HtmlText.CanonicalUrl(document));
    }

    // List items become paragraphs of their own; read as a whole block they would be glued together.
    private static IEnumerable<string> Paragraphs(IElement block)
    {
        var items = block.QuerySelectorAll("li");
        return items.Length > 0 ? items.Select(item => item.TextContent) : [block.TextContent];
    }

    // Boilerplate would also be matched against keywords: titles of related articles are other news.
    private static bool IsBoilerplate(IElement block)
    {
        var text = WithoutLeadingSymbols(HtmlText.CollapseWhitespace(block.TextContent));
        return text.Length == 0
            || IsLinksOnly(block, text)
            || IsRelatedContentLabel(text)
            || PromotionPrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    // Related-article lists, "follow us" calls and the reporting-suggestion box are made of links; emojis and
    // punctuation around the link do not count as text.
    private static bool IsLinksOnly(IElement block, string text)
    {
        var linkedText = string.Concat(block.QuerySelectorAll("a").Select(link => link.TextContent));
        return LettersAndDigits(text) == LettersAndDigits(linkedText);
    }

    // "LEIA TAMBÉM" alone or followed by a linked title ("LEIA TAMBÉM: Título de outra matéria").
    private static bool IsRelatedContentLabel(string text)
    {
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        return RelatedContentLabels.Contains((separator < 0 ? text : text[..separator]).Trim());
    }

    private static string WithoutLeadingSymbols(string text)
    {
        var start = 0;
        while (start < text.Length && !char.IsLetterOrDigit(text[start])) start++;
        return text[start..];
    }

    private static string LettersAndDigits(string value) => string.Concat(value.Where(char.IsLetterOrDigit));
}

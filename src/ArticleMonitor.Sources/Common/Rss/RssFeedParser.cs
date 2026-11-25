using System.ServiceModel.Syndication;
using System.Xml;
using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Sources.Common.Html;

namespace ArticleMonitor.Sources.Common.Rss;

/// <summary>Maps RSS 2.0 and Atom 1.0 items to discovered articles.</summary>
public static class RssFeedParser
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";

    // Feeds are remote input: no DTD processing and no external resolution (XXE).
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
    };

    public static IReadOnlyList<DiscoveredArticle> Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = XmlReader.Create(new StringReader(xml), ReaderSettings);
        var feed = SyndicationFeed.Load(reader);
        return [.. feed.Items.Select(ToArticle).OfType<DiscoveredArticle>()];
    }

    private static DiscoveredArticle? ToArticle(SyndicationItem item)
    {
        var url = ArticleUrl(item);
        return url is null ? null : new DiscoveredArticle(url, item.Title?.Text, Lead(item), PublishedAt(item));
    }

    private static string? ArticleUrl(SyndicationItem item)
    {
        var link = item.Links.FirstOrDefault(link => link.RelationshipType is null or "alternate")?.Uri;
        if (link is { IsAbsoluteUri: true }) return link.ToString();

        // RSS items without <link> may still carry a permalink <guid>.
        return Uri.TryCreate(item.Id, UriKind.Absolute, out var id) ? id.ToString() : null;
    }

    // Some feeds (e.g. G1) carry the lead in atom:subtitle and only an image plus teaser in the description.
    private static string? Lead(SyndicationItem item) => Subtitle(item) ?? HtmlText.ToPlainText(item.Summary?.Text);

    private static string? Subtitle(SyndicationItem item)
    {
        var extension = item.ElementExtensions
            .FirstOrDefault(element => element.OuterName == "subtitle" && element.OuterNamespace == AtomNamespace);
        if (extension is null) return null;

        using var reader = extension.GetReader();
        return reader.ReadElementContentAsString();
    }

    private static DateTimeOffset? PublishedAt(SyndicationItem item)
    {
        if (item.PublishDate != default) return item.PublishDate;
        return item.LastUpdatedTime != default ? item.LastUpdatedTime : null;
    }
}

namespace ArticleMonitor.Domain.Articles;

public sealed class Article
{
    public const int LanguageMaxLength = 16;

    private Article(int sourceId, CanonicalUrl url, ArticleHeadline headline, string language)
    {
        SourceId = sourceId;
        Url = url.Value;
        UrlHash = url.Hash;
        Title = headline.Title;
        Lead = headline.Lead;
        PublishedAt = headline.PublishedAt;
        Language = language;
    }

    // Used by EF Core.
    private Article()
    {
        Url = null!;
        UrlHash = null!;
        Title = null!;
        Language = null!;
    }

    public long Id { get; private set; }

    public int SourceId { get; private set; }

    public string Url { get; private set; }

    public UrlHash UrlHash { get; private set; }

    public string Title { get; private set; }

    public string? Lead { get; private set; }

    public string Language { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset CollectedAt { get; private set; }

    public string? Content { get; private set; }

    public ContentStatus ContentStatus { get; private set; }

    public static Article Collect(
        int sourceId,
        CanonicalUrl url,
        ArticleHeadline headline,
        string language,
        string? content,
        DateTimeOffset collectedAt)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(headline);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var hasContent = !string.IsNullOrWhiteSpace(content);
        return new Article(sourceId, url, headline, Text.Truncate(language.Trim(), LanguageMaxLength))
        {
            Content = hasContent ? content!.Trim() : null,
            ContentStatus = hasContent ? ContentStatus.Full : ContentStatus.LeadOnly,
            CollectedAt = collectedAt,
        };
    }
}

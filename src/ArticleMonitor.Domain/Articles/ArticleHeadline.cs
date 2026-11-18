using System.Diagnostics.CodeAnalysis;

namespace ArticleMonitor.Domain.Articles;

/// <summary>What a listing tells about an article before its page is read.</summary>
public sealed record ArticleHeadline
{
    public const int TitleMaxLength = 500;
    public const int LeadMaxLength = 2000;

    private ArticleHeadline(string title, string? lead, DateTimeOffset? publishedAt)
    {
        Title = title;
        Lead = lead;
        PublishedAt = publishedAt;
    }

    public string Title { get; }

    public string? Lead { get; }

    public DateTimeOffset? PublishedAt { get; }

    public static bool TryCreate(
        string? title, string? lead, DateTimeOffset? publishedAt, [NotNullWhen(true)] out ArticleHeadline? headline)
    {
        headline = null;
        var normalizedTitle = Text.NormalizeOptional(title, TitleMaxLength);
        if (normalizedTitle is null) return false;

        headline = new ArticleHeadline(normalizedTitle, Text.NormalizeOptional(lead, LeadMaxLength), publishedAt);
        return true;
    }
}

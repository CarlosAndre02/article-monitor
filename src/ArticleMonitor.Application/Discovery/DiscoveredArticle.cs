namespace ArticleMonitor.Application.Discovery;

/// <summary>An article as listed by its source (feed item, listing entry), before its page is read.</summary>
public sealed record DiscoveredArticle(string Url, string? Title, string? Lead, DateTimeOffset? PublishedAt);

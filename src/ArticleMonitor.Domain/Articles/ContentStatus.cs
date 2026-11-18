namespace ArticleMonitor.Domain.Articles;

public enum ContentStatus
{
    /// <summary>The article text was extracted from its page.</summary>
    Full,

    /// <summary>Only title and lead from the listing are known: page blocked, unreachable or not extractable.</summary>
    LeadOnly,
}

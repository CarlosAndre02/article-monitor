namespace ArticleMonitor.Domain.Collection;

/// <summary>Why discovery stopped asking the source for more articles.</summary>
public enum StopReason
{
    /// <summary>The source had nothing more to list (the normal end of an RSS feed).</summary>
    EndOfSource,

    /// <summary>Reached articles older than the watermark (minus overlap) or the initial lookback.</summary>
    ReachedLowerBound,

    /// <summary>Found a sequence of articles that were already collected.</summary>
    ReachedKnownItems,

    /// <summary>Safety cap on articles per run; older articles may be missing.</summary>
    ReachedItemCap,
}

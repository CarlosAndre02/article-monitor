namespace ArticleMonitor.Domain.Sources;

/// <summary>
/// Runtime state of a news source. Its definition (hosts, language, feeds) lives in the source module code.
/// </summary>
public sealed class Source
{
    private Source(SourceKey key)
    {
        Key = key;
        IsEnabled = true;
    }

    // Used by EF Core.
    private Source() => Key = null!;

    public int Id { get; private set; }

    public SourceKey Key { get; private set; }

    public bool IsEnabled { get; private set; }

    /// <summary>Start of the last run that reached its end; scheduled slots after it count as missed.</summary>
    public DateTimeOffset? LastCompletedRunStartedAt { get; private set; }

    /// <summary>Newest publication date seen by completed runs. Discovery stops a little before it.</summary>
    public DateTimeOffset? Watermark { get; private set; }

    /// <summary>Adapter-specific state (feed ETags, pagination cursors), opaque to the core.</summary>
    public string? DiscoveryState { get; private set; }

    public static Source Register(SourceKey key) => new(key);

    public void CompleteRun(DateTimeOffset runStartedAt, DateTimeOffset? newestPublishedAt, string? discoveryState)
    {
        LastCompletedRunStartedAt = runStartedAt;
        DiscoveryState = discoveryState;

        if (newestPublishedAt is null || newestPublishedAt <= Watermark) return;
        Watermark = newestPublishedAt;
    }

    /// <summary>
    /// A failed run still counts for scheduling, but keeps watermark and discovery state,
    /// so the next run covers the same window.
    /// </summary>
    public void RecordFailedRun(DateTimeOffset runStartedAt) => LastCompletedRunStartedAt = runStartedAt;
}

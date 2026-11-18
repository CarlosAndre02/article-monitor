namespace ArticleMonitor.Domain.Collection;

public enum RunStatus
{
    Running,
    Succeeded,

    /// <summary>Finished, but some feeds or pages failed.</summary>
    PartiallySucceeded,

    /// <summary>Nothing could be discovered because every request failed, or the run hit an unexpected error.</summary>
    Failed,

    /// <summary>Stopped before the end (shutdown or crash); the next run covers the same window again.</summary>
    Interrupted,
}

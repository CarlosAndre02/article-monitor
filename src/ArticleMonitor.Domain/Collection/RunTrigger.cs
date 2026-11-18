namespace ArticleMonitor.Domain.Collection;

public enum RunTrigger
{
    /// <summary>Started at a scheduled time.</summary>
    Scheduled,

    /// <summary>Started on application start because a scheduled time passed while it was not running.</summary>
    CatchUp,
}

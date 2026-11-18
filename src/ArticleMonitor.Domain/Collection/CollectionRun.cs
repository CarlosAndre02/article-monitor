using ArticleMonitor.Domain.Articles;

namespace ArticleMonitor.Domain.Collection;

/// <summary>Record of one collection run of a source: what was found, what was stored and what went wrong.</summary>
public sealed class CollectionRun
{
    public const int ErrorsMaxLength = 4000;

    private readonly List<string> _failureMessages = [];

    private CollectionRun(int sourceId, RunTrigger trigger, DateTimeOffset startedAt)
    {
        SourceId = sourceId;
        Trigger = trigger;
        StartedAt = startedAt;
        Status = RunStatus.Running;
    }

    // Used by EF Core.
    private CollectionRun()
    {
    }

    public long Id { get; private set; }

    public int SourceId { get; private set; }

    public RunTrigger Trigger { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public RunStatus Status { get; private set; }

    public StopReason? StopReason { get; private set; }

    /// <summary>True when the run may not have reached articles collected before, so some may be missing.</summary>
    public bool PossibleGap { get; private set; }

    public int Discovered { get; private set; }

    public int Added { get; private set; }

    public int Duplicates { get; private set; }

    /// <summary>Listed items ignored: invalid URL, host outside the source, or no title.</summary>
    public int Skipped { get; private set; }

    public int BlockedByRobots { get; private set; }

    public int ExtractionFallbacks { get; private set; }

    public int LeadOnly { get; private set; }

    public int Failures { get; private set; }

    public string? Errors { get; private set; }

    public static CollectionRun Start(int sourceId, RunTrigger trigger, DateTimeOffset startedAt) =>
        new(sourceId, trigger, startedAt);

    public void RecordDiscovered() => Discovered++;

    public void RecordSkipped() => Skipped++;

    public void RecordDuplicate() => Duplicates++;

    public void RecordBlockedByRobots() => BlockedByRobots++;

    public void RecordExtractionFallback() => ExtractionFallbacks++;

    public void RecordAdded(ContentStatus contentStatus)
    {
        Added++;
        if (contentStatus == ContentStatus.LeadOnly) LeadOnly++;
    }

    public void RecordFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Failures++;
        _failureMessages.Add(message);
    }

    public void Finish(StopReason stopReason, bool possibleGap, DateTimeOffset finishedAt)
    {
        Close(DetermineStatus(), finishedAt);
        StopReason = stopReason;
        PossibleGap = possibleGap;
    }

    public void Abort(string error, DateTimeOffset finishedAt)
    {
        RecordFailure(error);
        Close(RunStatus.Failed, finishedAt);
    }

    public void Interrupt(DateTimeOffset finishedAt) => Close(RunStatus.Interrupted, finishedAt);

    private RunStatus DetermineStatus()
    {
        if (Failures == 0) return RunStatus.Succeeded;
        return Discovered == 0 ? RunStatus.Failed : RunStatus.PartiallySucceeded;
    }

    private void Close(RunStatus status, DateTimeOffset finishedAt)
    {
        if (Status != RunStatus.Running)
        {
            throw new InvalidOperationException($"Collection run {Id} is already {Status}.");
        }

        Status = status;
        FinishedAt = finishedAt;
        Errors = _failureMessages.Count == 0
            ? null
            : Text.Truncate(string.Join(Environment.NewLine, _failureMessages), ErrorsMaxLength);
    }
}

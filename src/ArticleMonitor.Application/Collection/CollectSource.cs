using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace ArticleMonitor.Application.Collection;

/// <summary>Runs one collection of a source and records it, whatever happens.</summary>
public sealed partial class CollectSource(
    SourceHarvester harvester,
    ICollectionRunRepository runs,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<CollectSource> logger)
{
    public async Task<CollectionRun> ExecuteAsync(
        Source source, ISourceModule module, RunTrigger trigger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(module);

        var run = CollectionRun.Start(source.Id, trigger, clock.GetUtcNow());
        runs.Add(run);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var harvest = await harvester.HarvestAsync(new HarvestScope(source, module, run), cancellationToken);
            Complete(source, run, harvest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Interrupt(clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            // An unexpected failure of one source must not stop the others.
            LogRunAborted(logger, exception, source.Key.Value);
            run.Abort($"Unexpected {exception.GetType().Name}: {exception.Message}", clock.GetUtcNow());
            source.RecordFailedRun(run.StartedAt);
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        LogRunFinished(logger, source.Key.Value, run.Status, run.StopReason, run.Added, run.Duplicates, run.Failures);
        return run;
    }

    private void Complete(Source source, CollectionRun run, HarvestResult harvest)
    {
        run.Finish(harvest.Outcome.StopReason, harvest.Outcome.PossibleGap, clock.GetUtcNow());
        if (run.Status == RunStatus.Failed)
        {
            source.RecordFailedRun(run.StartedAt);
            return;
        }

        source.CompleteRun(run.StartedAt, harvest.NewestPublishedAt, harvest.DiscoveryState);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Collection of {Source} aborted by an unexpected error")]
    private static partial void LogRunAborted(ILogger logger, Exception exception, string source);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Collection of {Source} finished as {Status} ({StopReason}): {Added} added, "
            + "{Duplicates} duplicates, {Failures} failures")]
    private static partial void LogRunFinished(
        ILogger logger,
        string source,
        RunStatus status,
        StopReason? stopReason,
        int added,
        int duplicates,
        int failures);
}

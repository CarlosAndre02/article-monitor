using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArticleMonitor.Application.Collection;

public interface ICollectionCycle
{
    /// <summary>Recovers from a previous crash and registers source modules missing from the database.</summary>
    Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>Collects every enabled source whose latest scheduled time has no completed run yet.</summary>
    Task RunDueAsync(RunTrigger trigger, CancellationToken cancellationToken);
}

public sealed partial class CollectionCycle(
    IServiceScopeFactory scopeFactory,
    SourceModuleCatalog catalog,
    CollectionSchedule schedule,
    IOptions<CollectionOptions> options,
    TimeProvider clock,
    ILogger<CollectionCycle> logger) : ICollectionCycle
{
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var runs = services.GetRequiredService<ICollectionRunRepository>();
        var sources = services.GetRequiredService<ISourceRepository>();
        var interrupted = await InterruptStaleRunsAsync(runs, cancellationToken);
        var registered = await RegisterMissingSourcesAsync(sources, cancellationToken);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);

        LogPrepared(logger, interrupted, registered);
    }

    public async Task RunDueAsync(RunTrigger trigger, CancellationToken cancellationToken)
    {
        var dueSourceIds = await FindDueSourcesAsync(cancellationToken);
        if (dueSourceIds.Count == 0)
        {
            LogNothingDue(logger);
            return;
        }

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Value.MaxConcurrentSources,
            CancellationToken = cancellationToken,
        };
        await Parallel.ForEachAsync(
            dueSourceIds, parallel, (sourceId, token) => CollectAsync(sourceId, trigger, token));
    }

    private async Task<int> InterruptStaleRunsAsync(ICollectionRunRepository runs, CancellationToken cancellationToken)
    {
        // Runs still marked as running were cut by a crash or power loss: only one instance runs at a time.
        var staleRuns = await runs.ListRunningAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var run in staleRuns) run.Interrupt(now);
        return staleRuns.Count;
    }

    private async Task<int> RegisterMissingSourcesAsync(ISourceRepository sources, CancellationToken cancellationToken)
    {
        var existing = (await sources.ListAsync(cancellationToken)).Select(source => source.Key).ToHashSet();
        var missing = catalog.Keys.Where(key => !existing.Contains(key)).ToList();
        foreach (var key in missing) sources.Add(Source.Register(key));
        return missing.Count;
    }

    private async Task<IReadOnlyList<int>> FindDueSourcesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sources = await scope.ServiceProvider.GetRequiredService<ISourceRepository>().ListAsync(cancellationToken);
        var now = clock.GetUtcNow();

        return [.. sources
            .Where(source => source.IsEnabled && HasModule(source.Key))
            .Where(source => schedule.IsDue(source.LastCompletedRunStartedAt, now))
            .Select(source => source.Id)];
    }

    private bool HasModule(SourceKey key)
    {
        if (catalog.TryGet(key, out _)) return true;

        LogModuleMissing(logger, key.Value);
        return false;
    }

    private async ValueTask CollectAsync(int sourceId, RunTrigger trigger, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var source = await services.GetRequiredService<ISourceRepository>().FindAsync(sourceId, cancellationToken);
        if (source is null || !catalog.TryGet(source.Key, out var module)) return;

        try
        {
            await services.GetRequiredService<CollectSource>().ExecuteAsync(source, module, trigger, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Typically the database being unavailable while recording the run; other sources keep going.
            LogSourceFailed(logger, exception, source.Key.Value);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Collection prepared: {Interrupted} stale runs interrupted, {Registered} sources registered")]
    private static partial void LogPrepared(ILogger logger, int interrupted, int registered);

    [LoggerMessage(Level = LogLevel.Information, Message = "No source is due for collection")]
    private static partial void LogNothingDue(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning, Message = "Source {Source} is in the database but has no registered module")]
    private static partial void LogModuleMissing(ILogger logger, string source);

    [LoggerMessage(Level = LogLevel.Error, Message = "Collection of {Source} could not be recorded")]
    private static partial void LogSourceFailed(ILogger logger, Exception exception, string source);
}

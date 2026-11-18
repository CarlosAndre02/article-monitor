using ArticleMonitor.Application.Collection;
using ArticleMonitor.Domain.Collection;

namespace ArticleMonitor.Worker;

/// <summary>
/// Runs collections at the scheduled times. On start it catches up once if a scheduled time passed while the
/// application was off. Wall-clock time is checked every minute because long timers stop counting while the
/// machine sleeps, and would fire late after it wakes up.
/// </summary>
public sealed partial class CollectionScheduler(
    ICollectionCycle cycle,
    CollectionSchedule schedule,
    TimeProvider clock,
    ILogger<CollectionScheduler> logger) : BackgroundService
{
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    // Null until the start-up catch-up succeeds.
    private DateTimeOffset? _nextRunAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, clock);
        do
        {
            await TickAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        if (_nextRunAt is { } nextRunAt && clock.GetUtcNow() < nextRunAt) return;

        var trigger = _nextRunAt is null ? RunTrigger.CatchUp : RunTrigger.Scheduled;
        try
        {
            if (trigger == RunTrigger.CatchUp) await cycle.PrepareAsync(cancellationToken);
            await cycle.RunDueAsync(trigger, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Usually the database is not reachable yet (e.g. right after boot): retried at the next check.
            LogCycleFailed(logger, exception, trigger);
            return;
        }

        _nextRunAt = schedule.NextAfter(clock.GetUtcNow());
        LogNextRun(logger, _nextRunAt.Value);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Trigger} collection cycle failed; retrying at the next check")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception, RunTrigger trigger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Next collection at {NextRunAt:O}")]
    private static partial void LogNextRun(ILogger logger, DateTimeOffset nextRunAt);
}

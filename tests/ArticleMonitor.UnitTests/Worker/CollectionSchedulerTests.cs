using ArticleMonitor.Application.Collection;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArticleMonitor.UnitTests.Worker;

public class CollectionSchedulerTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly FakeTimeProvider _clock = new(SaoPauloTime(10, 9, 30));
    private readonly RecordingCycle _cycle = new();
    private readonly CollectionScheduler _scheduler;

    public CollectionSchedulerTests()
    {
        TimeOnly[] everyFourHours = [new(0, 0), new(4, 0), new(8, 0), new(12, 0), new(16, 0), new(20, 0)];
        var schedule = new CollectionSchedule(everyFourHours, SaoPaulo);
        _scheduler = new CollectionScheduler(_cycle, schedule, _clock, NullLogger<CollectionScheduler>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Prepares_and_catches_up_on_start()
    {
        await _scheduler.TickAsync(Ct);

        Assert.Equal(1, _cycle.Preparations);
        Assert.Equal([RunTrigger.CatchUp], _cycle.Triggers);
    }

    [Fact]
    public async Task Waits_for_the_next_scheduled_time()
    {
        await _scheduler.TickAsync(Ct);

        _clock.SetUtcNow(SaoPauloTime(10, 11, 59));
        await _scheduler.TickAsync(Ct);
        _clock.SetUtcNow(SaoPauloTime(10, 12, 0));
        await _scheduler.TickAsync(Ct);

        Assert.Equal([RunTrigger.CatchUp, RunTrigger.Scheduled], _cycle.Triggers);
    }

    [Fact]
    public async Task Runs_once_after_the_machine_slept_through_several_times()
    {
        await _scheduler.TickAsync(Ct);

        _clock.SetUtcNow(SaoPauloTime(11, 1, 0));
        await _scheduler.TickAsync(Ct);
        await _scheduler.TickAsync(Ct);

        Assert.Equal([RunTrigger.CatchUp, RunTrigger.Scheduled], _cycle.Triggers);
    }

    [Fact]
    public async Task Retries_the_catch_up_when_the_database_is_not_ready()
    {
        _cycle.FailNext = true;
        await _scheduler.TickAsync(Ct);

        _clock.Advance(CollectionScheduler.CheckInterval);
        await _scheduler.TickAsync(Ct);

        Assert.Equal(2, _cycle.Preparations);
        Assert.Equal([RunTrigger.CatchUp], _cycle.Triggers);
    }

    private static DateTimeOffset SaoPauloTime(int day, int hour, int minute) =>
        new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(-3));

    private sealed class RecordingCycle : ICollectionCycle
    {
        public bool FailNext { get; set; }

        public int Preparations { get; private set; }

        public List<RunTrigger> Triggers { get; } = [];

        public Task PrepareAsync(CancellationToken cancellationToken)
        {
            Preparations++;
            if (!FailNext) return Task.CompletedTask;

            FailNext = false;
            throw new InvalidOperationException("Database unavailable.");
        }

        public Task RunDueAsync(RunTrigger trigger, CancellationToken cancellationToken)
        {
            Triggers.Add(trigger);
            return Task.CompletedTask;
        }
    }
}

using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;

namespace ArticleMonitor.UnitTests.Domain;

public class CollectionRunTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FinishedAt = StartedAt.AddMinutes(3);

    [Fact]
    public void Starts_running()
    {
        var run = CollectionRun.Start(sourceId: 1, RunTrigger.CatchUp, StartedAt);

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(RunTrigger.CatchUp, run.Trigger);
        Assert.Null(run.FinishedAt);
    }

    [Fact]
    public void Succeeds_without_failures()
    {
        var run = CollectionRun.Start(1, RunTrigger.Scheduled, StartedAt);
        run.RecordDiscovered();
        run.RecordAdded(ContentStatus.Full);

        run.Finish(StopReason.ReachedKnownItems, possibleGap: false, FinishedAt);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(StopReason.ReachedKnownItems, run.StopReason);
        Assert.Equal(FinishedAt, run.FinishedAt);
        Assert.Null(run.Errors);
    }

    [Fact]
    public void Partially_succeeds_when_something_was_discovered_despite_failures()
    {
        var run = CollectionRun.Start(1, RunTrigger.Scheduled, StartedAt);
        run.RecordDiscovered();
        run.RecordFailure("Feed Política: HTTP 503");

        run.Finish(StopReason.EndOfSource, possibleGap: false, FinishedAt);

        Assert.Equal(RunStatus.PartiallySucceeded, run.Status);
        Assert.Contains("HTTP 503", run.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public void Fails_when_nothing_could_be_discovered()
    {
        var run = CollectionRun.Start(1, RunTrigger.Scheduled, StartedAt);
        run.RecordFailure("Feed Política: timeout");

        run.Finish(StopReason.EndOfSource, possibleGap: false, FinishedAt);

        Assert.Equal(RunStatus.Failed, run.Status);
    }

    [Fact]
    public void Abort_fails_the_run_with_the_error()
    {
        var run = CollectionRun.Start(1, RunTrigger.Scheduled, StartedAt);

        run.Abort("Unexpected error", FinishedAt);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(1, run.Failures);
        Assert.Equal("Unexpected error", run.Errors);
    }

    [Fact]
    public void Counts_articles_stored_without_text()
    {
        var run = CollectionRun.Start(1, RunTrigger.Scheduled, StartedAt);
        run.RecordAdded(ContentStatus.Full);
        run.RecordAdded(ContentStatus.LeadOnly);

        Assert.Equal(2, run.Added);
        Assert.Equal(1, run.LeadOnly);
    }

    [Fact]
    public void Truncates_long_error_digests()
    {
        var run = CollectionRun.Start(1, RunTrigger.Scheduled, StartedAt);
        for (var i = 0; i < 200; i++) run.RecordFailure($"https://g1.globo.com/artigo-{i}.ghtml: HTTP 500");

        run.Finish(StopReason.EndOfSource, possibleGap: false, FinishedAt);

        Assert.Equal(CollectionRun.ErrorsMaxLength, run.Errors!.Length);
    }

    [Fact]
    public void Cannot_be_closed_twice()
    {
        var run = CollectionRun.Start(1, RunTrigger.Scheduled, StartedAt);
        run.Interrupt(FinishedAt);

        Assert.Throws<InvalidOperationException>(() => run.Finish(StopReason.EndOfSource, false, FinishedAt));
    }
}

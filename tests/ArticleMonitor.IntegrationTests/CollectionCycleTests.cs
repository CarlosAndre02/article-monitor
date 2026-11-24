using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace ArticleMonitor.IntegrationTests;

public class CollectionCycleTests(SqlServerFixture sql)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preparation_registers_the_modules_once()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);

        await app.Cycle.PrepareAsync(Ct);
        await app.Cycle.PrepareAsync(Ct);

        var keys = await app.WithDbAsync(db => db.Sources.Select(source => source.Key).ToListAsync(Ct));
        Assert.Equal([FakeModule.Key], keys);
    }

    [Fact]
    public async Task Preparation_interrupts_runs_left_running_by_a_crash()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        await app.WithDbAsync(async db =>
        {
            var source = Source.Register(FakeModule.Key);
            db.Sources.Add(source);
            await db.SaveChangesAsync(Ct);
            var crashedRunStart = TestApplication.Start.AddHours(-3);
            db.CollectionRuns.Add(CollectionRun.Start(source.Id, RunTrigger.Scheduled, crashedRunStart));
            await db.SaveChangesAsync(Ct);
        });

        await app.Cycle.PrepareAsync(Ct);

        var status = await app.WithDbAsync(db => db.CollectionRuns.Select(run => run.Status).SingleAsync(Ct));
        Assert.Equal(RunStatus.Interrupted, status);
    }

    [Fact]
    public async Task A_catch_up_cycle_stores_articles_and_records_the_run()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        app.Module.Listing = Listing("a", "b", "c");

        await app.Cycle.PrepareAsync(Ct);
        await app.Cycle.RunDueAsync(RunTrigger.CatchUp, Ct);

        var articles = await app.WithDbAsync(db => db.Articles.OrderBy(article => article.Url).ToListAsync(Ct));
        Assert.Equal(3, articles.Count);
        Assert.All(articles, article => Assert.Equal(ContentStatus.Full, article.ContentStatus));
        Assert.Equal(FakePages.ArticleText(new Uri(articles[0].Url)), articles[0].Content);

        var run = await app.WithDbAsync(db => db.CollectionRuns.SingleAsync(Ct));
        Assert.Equal((RunStatus.Succeeded, RunTrigger.CatchUp, 3), (run.Status, run.Trigger, run.Added));

        var source = await app.WithDbAsync(db => db.Sources.SingleAsync(Ct));
        Assert.Equal(TestApplication.Start, source.LastCompletedRunStartedAt);
        Assert.Equal(TestApplication.Start.AddHours(-1), source.Watermark);
        Assert.Equal("{\"cursor\":1}", source.DiscoveryState);
    }

    [Fact]
    public async Task A_source_is_not_collected_again_before_the_next_scheduled_time()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        app.Module.Listing = Listing("a");
        await app.Cycle.PrepareAsync(Ct);
        await app.Cycle.RunDueAsync(RunTrigger.CatchUp, Ct);

        app.Clock.Advance(TimeSpan.FromHours(1));
        await app.Cycle.RunDueAsync(RunTrigger.Scheduled, Ct);

        Assert.Equal(1, await app.WithDbAsync(db => db.CollectionRuns.CountAsync(Ct)));
    }

    [Fact]
    public async Task A_disabled_source_is_not_collected()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        app.Module.Listing = Listing("a");
        await app.Cycle.PrepareAsync(Ct);
        await app.WithDbAsync(db => db.Sources.ExecuteUpdateAsync(
            setters => setters.SetProperty(source => source.IsEnabled, false), Ct));

        await app.Cycle.RunDueAsync(RunTrigger.CatchUp, Ct);

        Assert.Equal(0, await app.WithDbAsync(db => db.CollectionRuns.CountAsync(Ct)));
        Assert.Equal(0, await app.WithDbAsync(db => db.Articles.CountAsync(Ct)));
    }

    [Fact]
    public async Task The_next_scheduled_cycle_skips_articles_already_collected()
    {
        await using var app = await TestApplication.StartAsync(sql, Ct);
        app.Module.Listing = Listing("a", "b", "c");
        await app.Cycle.PrepareAsync(Ct);
        await app.Cycle.RunDueAsync(RunTrigger.CatchUp, Ct);

        app.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 10, 16, 0, 30, TimeSpan.FromHours(-3)));
        await app.Cycle.RunDueAsync(RunTrigger.Scheduled, Ct);

        var second = await app.WithDbAsync(db => db.CollectionRuns.OrderBy(run => run.Id).LastAsync(Ct));
        Assert.Equal(
            (RunTrigger.Scheduled, 0, 3, false), (second.Trigger, second.Added, second.Duplicates, second.PossibleGap));
        Assert.Equal(3, await app.WithDbAsync(db => db.Articles.CountAsync(Ct)));
    }

    // Newest first, one hour apart, starting one hour before the test start.
    private static DiscoveredArticle[] Listing(params string[] slugs) =>
    [
        .. slugs.Select((slug, index) => new DiscoveredArticle(
            $"https://news.example/{slug}", $"Título {slug}", "Lead.", TestApplication.Start.AddHours(-1 - index))),
    ];
}

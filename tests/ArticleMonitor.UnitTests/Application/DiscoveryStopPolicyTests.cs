using ArticleMonitor.Application.Collection;
using ArticleMonitor.Application.Discovery;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;

namespace ArticleMonitor.UnitTests.Application;

public class DiscoveryStopPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LowerBound = Now.AddDays(-2);

    [Fact]
    public void First_run_looks_back_the_initial_lookback()
    {
        var options = new CollectionOptions { InitialLookback = TimeSpan.FromDays(7) };

        var policy = DiscoveryStopPolicy.For(Source.Register(SourceKey.From("g1")), options, Now);

        Assert.Equal(Now.AddDays(-7), policy.LowerBound);
    }

    [Fact]
    public void Later_runs_stop_a_little_before_the_watermark()
    {
        var source = SourceWithWatermark(Now.AddHours(-4));
        var options = new CollectionOptions { WatermarkOverlap = TimeSpan.FromHours(2) };

        var policy = DiscoveryStopPolicy.For(source, options, Now);

        Assert.Equal(Now.AddHours(-6), policy.LowerBound);
    }

    [Fact]
    public void Never_goes_back_further_than_the_retention_window()
    {
        var source = SourceWithWatermark(Now.AddDays(-100));
        var options = new CollectionOptions { RetentionWindow = TimeSpan.FromDays(60) };

        var policy = DiscoveryStopPolicy.For(source, options, Now);

        Assert.Equal(Now.AddDays(-60), policy.LowerBound);
    }

    [Fact]
    public void Empty_database_collects_until_the_lower_bound()
    {
        var policy = NewPolicy(hasHistory: false);

        Assert.Equal(DiscoveryDecision.Collect, policy.Evaluate(Now.AddHours(-1), isKnown: false));
        Assert.Equal(DiscoveryDecision.Collect, policy.Evaluate(Now.AddDays(-1), isKnown: false));
        Assert.Equal(DiscoveryDecision.Stop, policy.Evaluate(Now.AddDays(-3), isKnown: false));
        Assert.Equal(new DiscoveryOutcome(StopReason.ReachedLowerBound, PossibleGap: false), policy.Complete());
    }

    [Fact]
    public void Stops_after_a_streak_of_known_articles()
    {
        var policy = NewPolicy(knownStreakToStop: 3);

        Assert.Equal(DiscoveryDecision.Collect, policy.Evaluate(Now, isKnown: false));
        Assert.Equal(DiscoveryDecision.SkipKnown, policy.Evaluate(Now, isKnown: true));
        Assert.Equal(DiscoveryDecision.SkipKnown, policy.Evaluate(Now, isKnown: true));
        Assert.Equal(DiscoveryDecision.SkipKnown, policy.Evaluate(Now, isKnown: true));
        Assert.Equal(DiscoveryDecision.Stop, policy.Evaluate(Now, isKnown: false));
        Assert.Equal(StopReason.ReachedKnownItems, policy.Complete().StopReason);
    }

    [Fact]
    public void A_known_story_pinned_on_top_does_not_stop_discovery()
    {
        var policy = NewPolicy(knownStreakToStop: 3);

        Assert.Equal(DiscoveryDecision.SkipKnown, policy.Evaluate(Now, isKnown: true));
        Assert.Equal(DiscoveryDecision.Collect, policy.Evaluate(Now, isKnown: false));
        Assert.Equal(DiscoveryDecision.Collect, policy.Evaluate(Now, isKnown: false));
    }

    [Fact]
    public void Known_articles_only_count_when_consecutive()
    {
        var policy = NewPolicy(knownStreakToStop: 2);

        Assert.Equal(DiscoveryDecision.SkipKnown, policy.Evaluate(Now, isKnown: true));
        Assert.Equal(DiscoveryDecision.Collect, policy.Evaluate(Now, isKnown: false));
        Assert.Equal(DiscoveryDecision.SkipKnown, policy.Evaluate(Now, isKnown: true));
        Assert.Equal(DiscoveryDecision.Collect, policy.Evaluate(Now, isKnown: false));
    }

    [Fact]
    public void Articles_without_date_are_never_cut_by_age()
    {
        var policy = NewPolicy();

        Assert.Equal(DiscoveryDecision.Collect, policy.Evaluate(publishedAt: null, isKnown: false));
    }

    [Fact]
    public void Stops_at_the_item_cap_and_flags_a_possible_gap()
    {
        var policy = NewPolicy(maxItems: 2);
        policy.Evaluate(Now, isKnown: false);
        policy.Evaluate(Now, isKnown: false);

        Assert.Equal(DiscoveryDecision.Stop, policy.Evaluate(Now, isKnown: false));
        Assert.Equal(new DiscoveryOutcome(StopReason.ReachedItemCap, PossibleGap: true), policy.Complete());
    }

    [Fact]
    public void Running_out_of_items_before_anything_known_flags_a_possible_gap()
    {
        var policy = NewPolicy(hasHistory: true);
        policy.Evaluate(Now, isKnown: false);
        policy.Evaluate(Now.AddHours(-1), isKnown: false);

        Assert.Equal(new DiscoveryOutcome(StopReason.EndOfSource, PossibleGap: true), policy.Complete());
    }

    [Fact]
    public void Running_out_of_items_after_reaching_known_ones_is_no_gap()
    {
        var policy = NewPolicy(hasHistory: true, knownStreakToStop: 5);
        policy.Evaluate(Now, isKnown: false);
        policy.Evaluate(Now.AddHours(-1), isKnown: true);

        Assert.Equal(new DiscoveryOutcome(StopReason.EndOfSource, PossibleGap: false), policy.Complete());
    }

    [Fact]
    public void Unchanged_sources_are_no_gap()
    {
        var policy = NewPolicy(hasHistory: true);

        Assert.Equal(new DiscoveryOutcome(StopReason.EndOfSource, PossibleGap: false), policy.Complete());
    }

    [Fact]
    public void Ending_right_after_the_streak_counts_as_reaching_known_items()
    {
        var policy = NewPolicy(knownStreakToStop: 2);
        policy.Evaluate(Now, isKnown: true);
        policy.Evaluate(Now, isKnown: true);

        Assert.Equal(StopReason.ReachedKnownItems, policy.Complete().StopReason);
    }

    private static DiscoveryStopPolicy NewPolicy(
        bool hasHistory = true, int knownStreakToStop = 5, int maxItems = 100) =>
        new(LowerBound, hasHistory, knownStreakToStop, maxItems);

    private static Source SourceWithWatermark(DateTimeOffset watermark)
    {
        var source = Source.Register(SourceKey.From("g1"));
        source.CompleteRun(watermark, watermark, discoveryState: null);
        return source;
    }
}

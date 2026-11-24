using ArticleMonitor.Domain.Collection;

namespace ArticleMonitor.UnitTests.Domain;

public class CollectionScheduleTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static readonly CollectionSchedule EveryFourHours = new(
        [new(0, 0), new(4, 0), new(8, 0), new(12, 0), new(16, 0), new(20, 0)], SaoPaulo);

    [Theory]
    [InlineData(10, 9, 30, 10, 12)]
    [InlineData(10, 20, 0, 11, 0)]
    [InlineData(10, 22, 15, 11, 0)]
    [InlineData(10, 0, 0, 10, 4)]
    public void Next_run_is_the_first_scheduled_time_strictly_after_now(
        int day, int hour, int minute, int expectedDay, int expectedHour)
    {
        var next = EveryFourHours.NextAfter(SaoPauloTime(day, hour, minute));
        Assert.Equal(SaoPauloTime(expectedDay, expectedHour), next);
    }

    [Theory]
    [InlineData(10, 9, 30, 10, 8)]
    [InlineData(10, 8, 0, 10, 8)]
    [InlineData(10, 0, 30, 10, 0)]
    public void Latest_scheduled_time_is_at_or_before_now(
        int day, int hour, int minute, int expectedDay, int expectedHour)
    {
        var latest = EveryFourHours.LatestAtOrBefore(SaoPauloTime(day, hour, minute));
        Assert.Equal(SaoPauloTime(expectedDay, expectedHour), latest);
    }

    [Fact]
    public void Latest_scheduled_time_may_be_on_the_previous_day()
    {
        var oncePerDay = new CollectionSchedule([new(8, 0)], SaoPaulo);
        Assert.Equal(SaoPauloTime(9, 8), oncePerDay.LatestAtOrBefore(SaoPauloTime(10, 7)));
    }

    [Fact]
    public void Times_are_read_in_the_configured_time_zone()
    {
        var oncePerDay = new CollectionSchedule([new(8, 0)], SaoPaulo);
        var next = oncePerDay.NextAfter(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 11, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void A_source_never_collected_is_due() => Assert.True(EveryFourHours.IsDue(null, SaoPauloTime(10, 9)));

    [Fact]
    public void A_source_collected_after_the_latest_scheduled_time_is_not_due() =>
        Assert.False(EveryFourHours.IsDue(SaoPauloTime(10, 8, 5), SaoPauloTime(10, 11, 59)));

    [Fact]
    public void A_run_started_exactly_at_the_scheduled_time_covers_it() =>
        Assert.False(EveryFourHours.IsDue(SaoPauloTime(10, 8), SaoPauloTime(10, 8, 1)));

    [Fact]
    public void A_source_becomes_due_when_the_next_scheduled_time_arrives() =>
        Assert.True(EveryFourHours.IsDue(SaoPauloTime(10, 8, 5), SaoPauloTime(10, 12)));

    [Fact]
    public void Times_missed_while_offline_lead_to_a_single_catch_up()
    {
        var offlineSince = SaoPauloTime(9, 10);
        var backOnline = SaoPauloTime(10, 15);

        Assert.True(EveryFourHours.IsDue(offlineSince, backOnline));
        Assert.False(EveryFourHours.IsDue(lastCompletedRunStartedAt: backOnline, now: SaoPauloTime(10, 15, 30)));
    }

    [Fact]
    public void Repeated_times_are_ignored()
    {
        var schedule = new CollectionSchedule([new(8, 0), new(8, 0)], SaoPaulo);
        Assert.Equal(SaoPauloTime(11, 8), schedule.NextAfter(SaoPauloTime(10, 8)));
    }

    [Fact]
    public void Needs_at_least_one_time() =>
        Assert.Throws<ArgumentException>(() => new CollectionSchedule([], SaoPaulo));

    private static DateTimeOffset SaoPauloTime(int day, int hour, int minute = 0) =>
        new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(-3));
}

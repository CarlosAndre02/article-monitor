namespace ArticleMonitor.Domain.Collection;

/// <summary>Fixed times of day, in a time zone, when sources are collected.</summary>
public sealed class CollectionSchedule
{
    // Days around the reference instant searched for occurrences; covers any set of times plus DST gaps.
    private const int DaysAround = 2;

    private readonly TimeOnly[] _times;
    private readonly TimeZoneInfo _timeZone;

    public CollectionSchedule(IEnumerable<TimeOnly> times, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(timeZone);

        _times = [.. times.Distinct().Order()];
        if (_times.Length == 0)
        {
            throw new ArgumentException("A schedule needs at least one time of day.", nameof(times));
        }

        _timeZone = timeZone;
    }

    public DateTimeOffset NextAfter(DateTimeOffset instant) =>
        OccurrencesAround(instant).First(occurrence => occurrence > instant);

    public DateTimeOffset LatestAtOrBefore(DateTimeOffset instant) =>
        OccurrencesAround(instant).Last(occurrence => occurrence <= instant);

    /// <summary>
    /// A source is due when no completed run started at or after the latest scheduled time.
    /// Several times missed while offline therefore lead to a single run.
    /// </summary>
    public bool IsDue(DateTimeOffset? lastCompletedRunStartedAt, DateTimeOffset now) =>
        lastCompletedRunStartedAt is null || lastCompletedRunStartedAt < LatestAtOrBefore(now);

    private IEnumerable<DateTimeOffset> OccurrencesAround(DateTimeOffset instant)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime);

        return Enumerable.Range(-DaysAround, (DaysAround * 2) + 1)
            .SelectMany(dayOffset => _times.Select(time => localDate.AddDays(dayOffset).ToDateTime(time)))
            .Where(localTime => !_timeZone.IsInvalidTime(localTime))
            .Select(localTime => new DateTimeOffset(localTime, _timeZone.GetUtcOffset(localTime)));
    }
}

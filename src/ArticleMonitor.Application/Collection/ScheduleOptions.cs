using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ArticleMonitor.Domain.Collection;

namespace ArticleMonitor.Application.Collection;

public sealed class ScheduleOptions
{
    public const string SectionName = "Collector:Schedule";

    /// <summary>Times of day ("HH:mm") when sources are collected.</summary>
    [Required]
    [MinLength(1)]
    public string[] Times { get; set; } = [];

    /// <summary>IANA time zone of <see cref="Times"/>, e.g. "America/Sao_Paulo".</summary>
    [Required]
    public string TimeZone { get; set; } = string.Empty;

    public bool TryCreateSchedule([NotNullWhen(true)] out CollectionSchedule? schedule)
    {
        schedule = null;
        if (!TryParseTimes(out var times)) return false;
        if (!System.TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out var timeZone)) return false;

        schedule = new CollectionSchedule(times, timeZone);
        return true;
    }

    private bool TryParseTimes(out List<TimeOnly> times)
    {
        times = [];
        foreach (var value in Times)
        {
            var parsed = TimeOnly.TryParseExact(
                value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
            if (!parsed) return false;

            times.Add(time);
        }

        return times.Count > 0;
    }
}

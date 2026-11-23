using System.ComponentModel.DataAnnotations;

namespace ArticleMonitor.Application.Collection;

public sealed class CollectionOptions
{
    public const string SectionName = "Collector";

    [Range(1, 32)]
    public int MaxConcurrentSources { get; set; } = 4;

    /// <summary>How far back the first run of a source goes, when nothing was collected before.</summary>
    [Range(typeof(TimeSpan), "01:00:00", "60.00:00:00")]
    public TimeSpan InitialLookback { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Margin below the watermark, for articles published with a retroactive timestamp.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan WatermarkOverlap { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Discovery stops after this many already collected articles in a row.</summary>
    [Range(1, 100)]
    public int KnownStreakToStop { get; set; } = 5;

    /// <summary>Safety cap on articles examined per run of a source.</summary>
    [Range(1, 10_000)]
    public int MaxItemsPerRun { get; set; } = 500;

    /// <summary>Articles older than this are never collected (matches the retention window).</summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "365.00:00:00")]
    public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromDays(60);

    /// <summary>Extracted texts shorter than this trigger the generic fallback extractor.</summary>
    [Range(0, 10_000)]
    public int MinExtractedChars { get; set; } = 200;

    /// <summary>Runs with fewer new articles never raise the content alert: one bad page would be too much.</summary>
    [Range(1, 10_000)]
    public int ContentAlertMinArticles { get; set; } = 5;

    /// <summary>Share of new articles missing the source's own extractor above which the run raises an alert.</summary>
    [Range(0.01, 1.0)]
    public double ContentAlertRate { get; set; } = 0.2;
}

using ArticleMonitor.Application.Collection;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;

namespace ArticleMonitor.Application.Discovery;

public enum DiscoveryDecision
{
    Collect,
    SkipKnown,
    Stop,
}

public sealed record DiscoveryOutcome(StopReason StopReason, bool PossibleGap);

/// <summary>
/// Decides, article by article in newest-first order, when discovery has gone far enough.
/// It is the same for every source, so adapters only need to know how to paginate.
/// </summary>
public sealed class DiscoveryStopPolicy
{
    private readonly bool _hasHistory;
    private readonly int _knownStreakToStop;
    private readonly int _maxItems;
    private int _evaluated;
    private int _knownSeen;
    private int _knownStreak;
    private StopReason? _stopReason;

    public DiscoveryStopPolicy(DateTimeOffset lowerBound, bool hasHistory, int knownStreakToStop, int maxItems)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(knownStreakToStop, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, 1);

        LowerBound = lowerBound;
        _hasHistory = hasHistory;
        _knownStreakToStop = knownStreakToStop;
        _maxItems = maxItems;
    }

    /// <summary>Articles published before this instant are not wanted.</summary>
    public DateTimeOffset LowerBound { get; }

    public static DiscoveryStopPolicy For(Source source, CollectionOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var oldestAllowed = now - options.RetentionWindow;
        var preferred = source.Watermark is { } watermark
            ? watermark - options.WatermarkOverlap
            : now - options.InitialLookback;

        return new DiscoveryStopPolicy(
            preferred > oldestAllowed ? preferred : oldestAllowed,
            hasHistory: source.Watermark is not null,
            options.KnownStreakToStop,
            options.MaxItemsPerRun);
    }

    public DiscoveryDecision Evaluate(DateTimeOffset? publishedAt, bool isKnown)
    {
        if (_stopReason is not null) return DiscoveryDecision.Stop;
        if (_evaluated >= _maxItems) return StopWith(StopReason.ReachedItemCap);

        // A streak, not a single known article: listings may pin old stories on top.
        if (_knownStreak >= _knownStreakToStop) return StopWith(StopReason.ReachedKnownItems);

        // Articles without a date cannot be judged by age; the streak and the cap still apply.
        if (publishedAt < LowerBound) return StopWith(StopReason.ReachedLowerBound);

        _evaluated++;
        if (!isKnown)
        {
            _knownStreak = 0;
            return DiscoveryDecision.Collect;
        }

        _knownSeen++;
        _knownStreak++;
        return DiscoveryDecision.SkipKnown;
    }

    public DiscoveryOutcome Complete()
    {
        var reason = _stopReason
            ?? (_knownStreak >= _knownStreakToStop ? StopReason.ReachedKnownItems : StopReason.EndOfSource);

        return new DiscoveryOutcome(reason, IsPossibleGap(reason));
    }

    private bool IsPossibleGap(StopReason reason) => reason switch
    {
        StopReason.ReachedItemCap => true,

        // The source ran out before reaching anything collected before: articles in between may be lost.
        StopReason.EndOfSource => _hasHistory && _evaluated > 0 && _knownSeen == 0,
        _ => false,
    };

    private DiscoveryDecision StopWith(StopReason reason)
    {
        _stopReason = reason;
        return DiscoveryDecision.Stop;
    }
}

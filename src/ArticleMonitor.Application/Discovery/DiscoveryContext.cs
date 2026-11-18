namespace ArticleMonitor.Application.Discovery;

/// <summary>
/// Per-run channel between the core and a discoverer: previous adapter state in; new state and failures out.
/// The new state is only persisted if the run completes, so an interrupted run never skips articles.
/// </summary>
public sealed class DiscoveryContext(string? previousState)
{
    private readonly List<string> _failures = [];

    public string? PreviousState { get; } = previousState;

    public string? NextState { get; private set; } = previousState;

    public IReadOnlyList<string> Failures => _failures;

    public void SaveState(string? state) => NextState = state;

    /// <summary>Reports a failure that does not stop discovery (e.g. one feed out of several is down).</summary>
    public void ReportFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _failures.Add(message);
    }
}

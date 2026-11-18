using System.Diagnostics.CodeAnalysis;

namespace ArticleMonitor.Application.Pages;

/// <summary>Downloads article pages through the shared HTTP pipeline.</summary>
public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken);
}

public enum PageFetchStatus
{
    Fetched,
    BlockedByRobots,
    Failed,
}

public sealed record PageFetchResult
{
    private PageFetchResult(PageFetchStatus status, string? html, string? error)
    {
        Status = status;
        Html = html;
        Error = error;
    }

    public PageFetchStatus Status { get; }

    public string? Html { get; }

    public string? Error { get; }

    [MemberNotNullWhen(true, nameof(Html))]
    public bool IsFetched => Status == PageFetchStatus.Fetched;

    public static PageFetchResult Fetched(string html) => new(PageFetchStatus.Fetched, html, error: null);

    public static PageFetchResult Blocked() => new(PageFetchStatus.BlockedByRobots, html: null, error: null);

    public static PageFetchResult Failed(string error) => new(PageFetchStatus.Failed, html: null, error);
}

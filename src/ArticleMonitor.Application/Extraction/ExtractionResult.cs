using System.Diagnostics.CodeAnalysis;

namespace ArticleMonitor.Application.Extraction;

public sealed record ExtractionResult
{
    private ExtractionResult(string? text, string? canonicalUrl, string? failureReason)
    {
        Text = text;
        CanonicalUrl = canonicalUrl;
        FailureReason = failureReason;
    }

    /// <summary>Article text, paragraphs separated by blank lines.</summary>
    public string? Text { get; }

    /// <summary>URL the page declares as canonical, if any.</summary>
    public string? CanonicalUrl { get; }

    public string? FailureReason { get; }

    [MemberNotNullWhen(true, nameof(Text))]
    public bool IsSuccess => Text is not null;

    public static ExtractionResult Succeeded(string text, string? canonicalUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new ExtractionResult(text, canonicalUrl, failureReason: null);
    }

    public static ExtractionResult Failed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ExtractionResult(text: null, canonicalUrl: null, reason);
    }
}

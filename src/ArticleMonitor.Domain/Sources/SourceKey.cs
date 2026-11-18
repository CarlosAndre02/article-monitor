using System.Text.RegularExpressions;

namespace ArticleMonitor.Domain.Sources;

/// <summary>Stable identifier shared by a source module definition and its database row.</summary>
public sealed partial record SourceKey
{
    public const int MaxLength = 50;

    private SourceKey(string value) => Value = value;

    public string Value { get; }

    public static SourceKey From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength || !KeyPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                $"Invalid source key '{value}'. Use lowercase letters, digits and single hyphens.", nameof(value));
        }

        return new SourceKey(normalized);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();
}

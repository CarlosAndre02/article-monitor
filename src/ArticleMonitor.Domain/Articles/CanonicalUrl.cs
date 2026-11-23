using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ArticleMonitor.Domain.Articles;

/// <summary>
/// Article URL normalized so the same article yields the same identity whatever feed or page linked to it.
/// </summary>
public sealed class CanonicalUrl : IEquatable<CanonicalUrl>
{
    public const int MaxLength = 2048;

    private const string TrackingParameterPrefix = "utm_";

    private static readonly HashSet<string> TrackingParameters =
        new(StringComparer.OrdinalIgnoreCase) { "fbclid", "gclid" };

    private CanonicalUrl(string value, string host)
    {
        Value = value;
        Host = host;
        Hash = UrlHash.Of(value);
    }

    public string Value { get; }

    public string Host { get; }

    public UrlHash Hash { get; }

    public static bool TryCreate(string? raw, [NotNullWhen(true)] out CanonicalUrl? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.IdnHost.ToLowerInvariant();
        var normalized = Normalize(uri, host);
        if (normalized.Length > MaxLength) return false;

        url = new CanonicalUrl(normalized, host);
        return true;
    }

    public bool IsOnAnyHost(IEnumerable<string> hosts) =>
        hosts.Any(host => string.Equals(host, Host, StringComparison.OrdinalIgnoreCase));

    public Uri ToUri() => new(Value);

    public bool Equals(CanonicalUrl? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CanonicalUrl);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    private static string Normalize(Uri uri, string host)
    {
        var builder = new StringBuilder(uri.Scheme).Append("://").Append(host);
        if (!uri.IsDefaultPort) builder.Append(':').Append(uri.Port);
        builder.Append(uri.AbsolutePath);

        var query = NormalizeQuery(uri.Query);
        if (query.Length > 0) builder.Append('?').Append(query);

        return builder.ToString();
    }

    private static string NormalizeQuery(string query)
    {
        if (query.Length == 0) return query;

        // Only the first "?" is the delimiter: any other belongs to the parameters.
        var parameters = query[1..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !IsTracking(ParameterName(parameter)))
            .Order(StringComparer.Ordinal);

        return string.Join('&', parameters);
    }

    private static string ParameterName(string parameter)
    {
        var separator = parameter.IndexOf('=', StringComparison.Ordinal);
        return separator < 0 ? parameter : parameter[..separator];
    }

    private static bool IsTracking(string name) =>
        name.StartsWith(TrackingParameterPrefix, StringComparison.OrdinalIgnoreCase)
        || TrackingParameters.Contains(name);
}

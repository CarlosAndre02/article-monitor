using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArticleMonitor.Sources.Common.Rss;

/// <summary>HTTP validators of a feed, sent back so an unchanged feed answers 304 without a body.</summary>
public sealed record FeedValidators(string? ETag, DateTimeOffset? LastModified)
{
    [JsonIgnore]
    public bool IsEmpty => ETag is null && LastModified is null;

    public static FeedValidators From(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new FeedValidators(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified);
    }

    public void ApplyTo(HttpRequestHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (ETag is not null && EntityTagHeaderValue.TryParse(ETag, out var entityTag))
        {
            headers.IfNoneMatch.Add(entityTag);
        }

        if (LastModified is not null) headers.IfModifiedSince = LastModified;
    }
}

/// <summary>Discovery state of an RSS source: validators per feed URL, stored by the core as opaque JSON.</summary>
internal sealed class RssDiscoveryState
{
    private readonly Dictionary<string, FeedValidators> _feeds;

    private RssDiscoveryState(Dictionary<string, FeedValidators> feeds) => _feeds = feeds;

    public static RssDiscoveryState Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty();

        try
        {
            return new RssDiscoveryState(JsonSerializer.Deserialize<Dictionary<string, FeedValidators>>(json) ?? []);
        }
        catch (JsonException)
        {
            // Unreadable state (e.g. written by another adapter) only costs one full, unconditional read.
            return Empty();
        }
    }

    public FeedValidators? For(Uri feedUrl) => _feeds.GetValueOrDefault(feedUrl.ToString());

    public void Update(Uri feedUrl, FeedValidators validators)
    {
        if (validators.IsEmpty)
        {
            _feeds.Remove(feedUrl.ToString());
            return;
        }

        _feeds[feedUrl.ToString()] = validators;
    }

    public string Serialize() => JsonSerializer.Serialize(_feeds);

    private static RssDiscoveryState Empty() => new([]);
}

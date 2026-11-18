using ArticleMonitor.Domain.Sources;

namespace ArticleMonitor.Application.Sources;

/// <summary>Static description of a news source, declared in code by its module.</summary>
public sealed class SourceDefinition
{
    public SourceDefinition(SourceKey key, string name, string language, IReadOnlyCollection<string> allowedHosts)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(allowedHosts);
        if (allowedHosts.Count == 0 || allowedHosts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A source needs at least one valid host.", nameof(allowedHosts));
        }

        Key = key;
        Name = name;
        Language = language;
        AllowedHosts = [.. allowedHosts.Select(host => host.Trim().ToLowerInvariant())];
    }

    public SourceKey Key { get; }

    public string Name { get; }

    /// <summary>BCP 47 language tag of the source articles, e.g. "pt-BR".</summary>
    public string Language { get; }

    /// <summary>Hosts articles may live on; links to other hosts are ignored.</summary>
    public IReadOnlyList<string> AllowedHosts { get; }
}

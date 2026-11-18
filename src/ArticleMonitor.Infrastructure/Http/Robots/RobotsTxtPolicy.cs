using System.Text.RegularExpressions;

namespace ArticleMonitor.Infrastructure.Http.Robots;

/// <summary>
/// The rules of a robots.txt that apply to one user agent (RFC 9309 subset: groups, Allow/Disallow,
/// longest match, "*" and "$"). Crawl-delay is not read: the per-host rate limit already spaces requests.
/// </summary>
internal sealed class RobotsTxtPolicy
{
    private const string AnyAgent = "*";

    private readonly IReadOnlyList<RobotsRule> _rules;

    private RobotsTxtPolicy(IReadOnlyList<RobotsRule> rules) => _rules = rules;

    public static RobotsTxtPolicy AllowAll { get; } = new([]);

    public static RobotsTxtPolicy DisallowAll { get; } = new([RobotsRule.Create("/", allow: false)!]);

    public static RobotsTxtPolicy Parse(string content, string productToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(productToken);

        var groups = RobotsTxtGroups.Read(content);
        var matching = groups.Where(group => group.Targets(productToken)).ToList();
        if (matching.Count == 0) matching = [.. groups.Where(group => group.Targets(AnyAgent))];

        return new RobotsTxtPolicy([.. matching.SelectMany(group => group.Rules)]);
    }

    public bool IsAllowed(string pathAndQuery)
    {
        var path = string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery;

        // The most specific (longest) matching rule wins; on a tie, Allow wins.
        var decisive = _rules
            .Where(rule => rule.Matches(path))
            .OrderByDescending(rule => rule.Specificity)
            .ThenByDescending(rule => rule.Allow)
            .FirstOrDefault();

        return decisive?.Allow ?? true;
    }
}

internal sealed class RobotsRule
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Regex _pattern;

    private RobotsRule(Regex pattern, int specificity, bool allow)
    {
        _pattern = pattern;
        Specificity = specificity;
        Allow = allow;
    }

    public int Specificity { get; }

    public bool Allow { get; }

    /// <summary>Returns null for an empty path, which per RFC 9309 matches nothing.</summary>
    public static RobotsRule? Create(string path, bool allow)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var anchored = path.EndsWith('$');
        var body = anchored ? path[..^1] : path;
        var regex = "^" + string.Join(".*", body.Split('*').Select(Regex.Escape)) + (anchored ? "$" : string.Empty);

        // robots.txt is remote input: a non-backtracking engine and a timeout keep matching cheap.
        var pattern = new Regex(regex, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, MatchTimeout);
        return new RobotsRule(pattern, path.Length, allow);
    }

    public bool Matches(string path) => _pattern.IsMatch(path);
}

internal sealed class RobotsTxtGroup
{
    public List<string> UserAgents { get; } = [];

    public List<RobotsRule> Rules { get; } = [];

    public bool Targets(string agent) =>
        UserAgents.Any(userAgent => string.Equals(userAgent, agent, StringComparison.OrdinalIgnoreCase));
}

internal static class RobotsTxtGroups
{
    public static List<RobotsTxtGroup> Read(string content)
    {
        var groups = new List<RobotsTxtGroup>();
        RobotsTxtGroup? current = null;
        var collectingAgents = false;

        foreach (var (field, value) in Records(content))
        {
            if (field == "user-agent")
            {
                // Consecutive user-agent lines share one group; a user-agent after rules starts a new one.
                if (current is null || !collectingAgents) groups.Add(current = new RobotsTxtGroup());
                current.UserAgents.Add(value);
                collectingAgents = true;
                continue;
            }

            if (field is not ("allow" or "disallow")) continue;

            collectingAgents = false;
            if (RobotsRule.Create(value, allow: field == "allow") is { } rule) current?.Rules.Add(rule);
        }

        return groups;
    }

    private static IEnumerable<(string Field, string Value)> Records(string content)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var commentStart = rawLine.IndexOf('#', StringComparison.Ordinal);
            var line = (commentStart < 0 ? rawLine : rawLine[..commentStart]).Trim();
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0) continue;

            yield return (line[..separator].Trim().ToLowerInvariant(), line[(separator + 1)..].Trim());
        }
    }
}

using System.Globalization;
using ArticleMonitor.Application.Articles;

namespace ArticleMonitor.Worker;

/// <summary>
/// <c>articles [N]</c>: prints the N articles stored most recently, with link and text, so the extraction can be
/// checked against the page in the browser. The text is compressed in the database, so plain SQL cannot show it.
/// </summary>
internal static class ArticlesCommand
{
    public const string Name = "articles";
    public const int DefaultCount = 5;
    public const int MaxCount = 50;

    public const int Success = 0;
    public const int UsageError = 2;

    private const string Separator = "────────────────────────────────────────";

    private static readonly string Usage = $"Usage: {Name} [N]  (N from 1 to {MaxCount}, default {DefaultCount})";

    public static bool IsInvoked(string[] args) => args is [Name, ..];

    public static async Task<int> RunAsync(
        IServiceProvider services, string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParseCount(args, out var count))
        {
            await error.WriteLineAsync(Usage);
            return UsageError;
        }

        await using var scope = services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IArticleQueries>();
        Write(output, await queries.ListLatestAsync(count, CancellationToken.None));
        return Success;
    }

    public static bool TryParseCount(string[] args, out int count)
    {
        count = DefaultCount;
        return args switch
        {
            [Name] => true,
            [Name, var raw] => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out count)
                && count is >= 1 and <= MaxCount,
            _ => false,
        };
    }

    public static void Write(TextWriter output, IReadOnlyList<CollectedArticle> articles)
    {
        if (articles.Count == 0)
        {
            output.WriteLine("No articles stored yet.");
            return;
        }

        foreach (var article in articles) WriteArticle(output, article);
    }

    private static void WriteArticle(TextWriter output, CollectedArticle article)
    {
        output.WriteLine($"#{article.Id} · {article.Source} · {article.ContentStatus}");
        output.WriteLine(TerminalText.Safe(article.Title));
        output.WriteLine(article.Url);
        output.WriteLine($"Published: {Format(article.PublishedAt)} · Collected: {Format(article.CollectedAt)}");
        output.WriteLine();
        if (article.Lead is { } lead) output.WriteLine($"Lead: {TerminalText.Safe(lead)}{Environment.NewLine}");
        output.WriteLine(article.Content is { } content ? TerminalText.Safe(content) : "(no text, lead only)");
        output.WriteLine(Separator);
    }

    private static string Format(DateTimeOffset? instant) =>
        instant?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture) ?? "unknown";
}

/// <summary>
/// Article text comes from the sources. Printed as is, an escape sequence in it would be run by the terminal
/// (colors, cursor moves, hidden or rewritten lines), and bidi controls could make it read differently.
/// </summary>
internal static class TerminalText
{
    public static string Safe(string text) => new([.. text.Where(IsSafe)]);

    private static bool IsSafe(char character) =>
        character is '\n' or '\t' || !(char.IsControl(character) || IsBidiControl(character));

    private static bool IsBidiControl(char character) =>
        character is '؜' or '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
}

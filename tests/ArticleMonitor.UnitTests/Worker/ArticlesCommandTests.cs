using ArticleMonitor.Application.Articles;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Worker;

namespace ArticleMonitor.UnitTests.Worker;

public class ArticlesCommandTests
{
    [Fact]
    public void Lists_the_default_count_without_a_number()
    {
        Assert.True(ArticlesCommand.TryParseCount(["articles"], out var count));
        Assert.Equal(ArticlesCommand.DefaultCount, count);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("50", 50)]
    public void Accepts_a_count_within_the_limit(string raw, int expected)
    {
        Assert.True(ArticlesCommand.TryParseCount(["articles", raw], out var count));
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("-1")]
    [InlineData("1e3")]
    [InlineData("cinco")]
    public void Rejects_any_other_count(string raw) =>
        Assert.False(ArticlesCommand.TryParseCount(["articles", raw], out _));

    [Fact]
    public void Rejects_extra_arguments() =>
        Assert.False(ArticlesCommand.TryParseCount(["articles", "5", "6"], out _));

    [Fact]
    public void Prints_the_link_and_the_text_of_each_article()
    {
        var output = Print(Article(1, "Primeiro texto."), Article(2, content: null));

        Assert.Contains("#1 · g1 · Full", output, StringComparison.Ordinal);
        Assert.Contains("https://g1.globo.com/a1", output, StringComparison.Ordinal);
        Assert.Contains("Primeiro texto.", output, StringComparison.Ordinal);
        Assert.Contains("(no text, lead only)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Never_prints_terminal_escapes_or_bidi_controls_from_the_source()
    {
        var output = Print(Article(1, "Texto\u001b[2J limpo‮ invertido.\nSegunda linha."));

        Assert.Contains("Texto[2J limpo invertido.\nSegunda linha.", output, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', output);
        Assert.DoesNotContain('‮', output);
    }

    [Fact]
    public void Says_so_when_nothing_was_stored() =>
        Assert.Contains("No articles stored yet.", Print(), StringComparison.Ordinal);

    private static string Print(params CollectedArticle[] articles)
    {
        using var output = new StringWriter();
        ArticlesCommand.Write(output, articles);
        return output.ToString();
    }

    private static CollectedArticle Article(long id, string? content) =>
        new(
            id,
            "g1",
            $"https://g1.globo.com/a{id}",
            $"Título {id}",
            Lead: null,
            PublishedAt: null,
            CollectedAt: DateTimeOffset.UnixEpoch,
            content is null ? ContentStatus.LeadOnly : ContentStatus.Full,
            content);
}

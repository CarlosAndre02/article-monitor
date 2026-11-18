using ArticleMonitor.Infrastructure.Http.Robots;

namespace ArticleMonitor.UnitTests.Infrastructure;

public class RobotsTxtPolicyTests
{
    private const string Agent = "article-monitor";

    // Excerpt of g1.globo.com/robots.txt.
    private const string G1Robots = """
        User-Agent: *
        Disallow: /jornalismo/g1/
        Disallow: /busca/*
        Disallow: /teste-*.html$
        Disallow: /beta/
        Disallow: *globo-cdn-src/*
        Sitemap: https://g1.globo.com/sitemap/g1/sitemap.xml
        """;

    [Theory]
    [InlineData("/politica/noticia/2026/11/10/materia.ghtml", true)]
    [InlineData("/rss/g1/politica/", true)]
    [InlineData("/busca/?q=inflacao", false)]
    [InlineData("/beta/pagina", false)]
    [InlineData("/teste-layout.html", false)]
    [InlineData("/teste-layout.html?versao=2", true)]
    [InlineData("/assets/globo-cdn-src/app.js", false)]
    public void Applies_the_g1_rules(string path, bool allowed) =>
        Assert.Equal(allowed, RobotsTxtPolicy.Parse(G1Robots, Agent).IsAllowed(path));

    [Theory]
    [InlineData("/pasta/publico/pagina", true)]
    [InlineData("/pasta/privado", false)]
    public void The_longest_matching_rule_wins(string path, bool allowed)
    {
        var policy = RobotsTxtPolicy.Parse("User-agent: *\nDisallow: /pasta\nAllow: /pasta/publico", Agent);
        Assert.Equal(allowed, policy.IsAllowed(path));
    }

    [Fact]
    public void Allow_wins_a_tie()
    {
        var policy = RobotsTxtPolicy.Parse("User-agent: *\nDisallow: /pagina\nAllow: /pagina", Agent);
        Assert.True(policy.IsAllowed("/pagina"));
    }

    [Fact]
    public void A_group_for_our_agent_replaces_the_generic_one()
    {
        const string robots = """
            User-agent: *
            Disallow: /

            User-agent: Article-Monitor
            Disallow: /privado
            """;
        var policy = RobotsTxtPolicy.Parse(robots, Agent);

        Assert.True(policy.IsAllowed("/noticia"));
        Assert.False(policy.IsAllowed("/privado/x"));
    }

    [Fact]
    public void Consecutive_user_agent_lines_share_their_rules()
    {
        var policy = RobotsTxtPolicy.Parse("User-agent: outro-bot\nUser-agent: article-monitor\nDisallow: /x", Agent);
        Assert.False(policy.IsAllowed("/x"));
    }

    [Fact]
    public void Ignores_comments_and_empty_disallow()
    {
        var policy = RobotsTxtPolicy.Parse("# regras\nUser-agent: * # todos\nDisallow:", Agent);
        Assert.True(policy.IsAllowed("/qualquer"));
    }

    [Fact]
    public void Allows_everything_without_a_matching_group() =>
        Assert.True(RobotsTxtPolicy.Parse("User-agent: outro-bot\nDisallow: /", Agent).IsAllowed("/x"));

    [Fact]
    public void Disallow_all_blocks_every_path() => Assert.False(RobotsTxtPolicy.DisallowAll.IsAllowed("/x"));
}

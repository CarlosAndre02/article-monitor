using ArticleMonitor.Domain.Articles;

namespace ArticleMonitor.UnitTests.Domain;

public class CanonicalUrlTests
{
    [Theory]
    [InlineData("HTTPS://G1.Globo.com/politica/noticia/a.ghtml", "https://g1.globo.com/politica/noticia/a.ghtml")]
    [InlineData("https://g1.globo.com/a.ghtml#comentarios", "https://g1.globo.com/a.ghtml")]
    [InlineData("https://g1.globo.com/a.ghtml?utm_source=x&utm_medium=y", "https://g1.globo.com/a.ghtml")]
    [InlineData("https://g1.globo.com/a.ghtml?b=2&fbclid=z&a=1&gclid=w", "https://g1.globo.com/a.ghtml?a=1&b=2")]
    [InlineData("https://g1.globo.com:443/a.ghtml", "https://g1.globo.com/a.ghtml")]
    [InlineData("http://g1.globo.com:8080/a", "http://g1.globo.com:8080/a")]
    [InlineData("  https://g1.globo.com/Caminho/Com/Maiusculas  ", "https://g1.globo.com/Caminho/Com/Maiusculas")]
    [InlineData("https://g1.globo.com/a?b&?c", "https://g1.globo.com/a??c&b")]
    [InlineData("https://g1.globo.com/a??c&b", "https://g1.globo.com/a??c&b")]
    public void Normalizes_the_url(string raw, string expected)
    {
        Assert.True(CanonicalUrl.TryCreate(raw, out var url));
        Assert.Equal(expected, url.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/politica/noticia/a.ghtml")]
    [InlineData("ftp://g1.globo.com/a")]
    [InlineData("javascript:alert(1)")]
    public void Rejects_what_is_not_an_absolute_http_url(string? raw) =>
        Assert.False(CanonicalUrl.TryCreate(raw, out _));

    [Fact]
    public void Rejects_urls_longer_than_the_column()
    {
        var raw = "https://g1.globo.com/" + new string('a', CanonicalUrl.MaxLength);
        Assert.False(CanonicalUrl.TryCreate(raw, out _));
    }

    [Fact]
    public void Links_to_the_same_article_share_the_hash()
    {
        CanonicalUrl.TryCreate("https://g1.globo.com/a.ghtml?utm_source=rss", out var fromFeed);
        CanonicalUrl.TryCreate("https://G1.globo.com/a.ghtml#topo", out var fromPage);

        Assert.Equal(fromFeed!.Hash, fromPage!.Hash);
    }

    [Fact]
    public void Different_articles_have_different_hashes()
    {
        CanonicalUrl.TryCreate("https://g1.globo.com/a.ghtml", out var first);
        CanonicalUrl.TryCreate("https://g1.globo.com/b.ghtml", out var second);

        Assert.NotEqual(first!.Hash, second!.Hash);
    }

    [Theory]
    [InlineData("https://g1.globo.com/a.ghtml", true)]
    [InlineData("https://ge.globo.com/a.ghtml", false)]
    public void Knows_whether_it_is_on_the_source_hosts(string raw, bool expected)
    {
        CanonicalUrl.TryCreate(raw, out var url);
        Assert.Equal(expected, url!.IsOnAnyHost(["g1.globo.com"]));
    }

    [Fact]
    public void Is_on_the_source_when_any_of_its_hosts_matches()
    {
        CanonicalUrl.TryCreate("https://g1.globo.com/a.ghtml", out var url);
        Assert.True(url!.IsOnAnyHost(["ge.globo.com", "g1.globo.com"]));
    }
}

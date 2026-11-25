using ArticleMonitor.Sources.G1;

namespace ArticleMonitor.Sources.Tests.G1;

public class G1SourceModuleTests
{
    [Fact]
    public void Declares_g1_in_portuguese_on_its_own_host()
    {
        var definition = G1SourceModule.SourceDefinition;

        Assert.Equal("g1", definition.Key.Value);
        Assert.Equal("pt-BR", definition.Language);
        Assert.Equal(["g1.globo.com"], definition.AllowedHosts);
    }

    [Fact]
    public void Follows_the_politics_economy_and_technology_feeds()
    {
        string[] expected =
        [
            "https://g1.globo.com/rss/g1/politica/",
            "https://g1.globo.com/rss/g1/economia/",
            "https://g1.globo.com/rss/g1/tecnologia/",
        ];

        Assert.Equal(expected, G1SourceModule.Feeds.Select(feed => feed.Url.ToString()));
    }
}

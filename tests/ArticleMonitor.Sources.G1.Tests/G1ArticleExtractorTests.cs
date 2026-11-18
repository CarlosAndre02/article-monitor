using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Sources.Testing;

namespace ArticleMonitor.Sources.G1.Tests;

public class G1ArticleExtractorTests : ArticleContentExtractorContractTests
{
    private const string ExpectedText = """
        O Senado aprovou nesta terça-feira (10) um projeto fictício sobre relatórios de uso de dados.

        O que muda

        Pelo texto, os relatórios devem trazer, segundo a versão aprovada:

        quais dados foram coletados;

        com quem foram compartilhados.

        “É um passo para a transparência”, afirmou o relator fictício.
        """;

    protected override string ArticlePageHtml => Fixtures.Read("article.html");

    protected override Uri ArticlePageUrl { get; } =
        new("https://g1.globo.com/politica/noticia/2026/11/10/senado-aprova-projeto-ficticio.ghtml");

    [Fact]
    public void Reads_paragraphs_intertitles_list_items_and_quotes_in_order()
    {
        var result = CreateExtractor().Extract(ArticlePageHtml, ArticlePageUrl);

        Assert.Equal(ExpectedText, result.Text);
    }

    // Related-article titles would match keywords of other news, so they must stay out of the text.
    [Theory]
    [InlineData("sugestão de reportagem")]
    [InlineData("Favorite o g1")]
    [InlineData("LEIA TAMBÉM")]
    [InlineData("Outra matéria fictícia")]
    [InlineData("Saiba quem é o servidor fictício")]
    [InlineData("Clique aqui para seguir")]
    [InlineData("Mande para o g1")]
    [InlineData("Leia mais notícias sobre a região")]
    [InlineData("Veja também")]
    [InlineData("Legenda fictícia")]
    [InlineData("Menu do g1")]
    public void Leaves_out_boilerplate_related_links_and_page_chrome(string excerpt)
    {
        var text = CreateExtractor().Extract(ArticlePageHtml, ArticlePageUrl).Text!;

        Assert.DoesNotContain(excerpt, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reads_the_canonical_url()
    {
        var result = CreateExtractor().Extract(ArticlePageHtml, ArticlePageUrl);
        Assert.Equal(ArticlePageUrl.ToString(), result.CanonicalUrl);
    }

    [Fact]
    public void Fails_when_the_layout_changes_so_the_core_tries_the_generic_extractor()
    {
        const string newLayout = "<html><body><article><p>Texto em um layout novo do g1.</p></article></body></html>";

        Assert.False(CreateExtractor().Extract(newLayout, ArticlePageUrl).IsSuccess);
    }

    protected override IArticleContentExtractor CreateExtractor() => new G1ArticleExtractor();
}

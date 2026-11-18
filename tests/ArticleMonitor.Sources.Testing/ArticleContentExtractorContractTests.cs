using ArticleMonitor.Application.Extraction;
using Xunit;

namespace ArticleMonitor.Sources.Testing;

/// <summary>
/// Rules every <see cref="IArticleContentExtractor"/> must follow. A source module's test project inherits this
/// class and provides a fixture of one of its article pages.
/// </summary>
public abstract class ArticleContentExtractorContractTests
{
    [Fact]
    public void Extracts_text_from_an_article_page()
    {
        var result = CreateExtractor().Extract(ArticlePageHtml, ArticlePageUrl);

        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text, not html")]
    [InlineData("<html><body><nav>Menu</nav><footer>Rodapé</footer></body></html>")]
    public void Returns_a_failure_instead_of_throwing_on_pages_without_an_article(string html) =>
        Assert.False(CreateExtractor().Extract(html, ArticlePageUrl).IsSuccess);

    protected abstract string ArticlePageHtml { get; }

    protected abstract Uri ArticlePageUrl { get; }

    protected abstract IArticleContentExtractor CreateExtractor();
}

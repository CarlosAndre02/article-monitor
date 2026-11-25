using ArticleMonitor.Application.Collection;
using ArticleMonitor.Application.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArticleMonitor.UnitTests.Application;

public class ArticleContentReaderTests
{
    private static readonly Uri PageUrl = new("https://news.example/a");

    private readonly FakeTimeProvider _clock = new();
    private readonly RecordingLogger<ArticleContentReader> _logger = new();

    [Fact]
    public void Uses_the_module_extractor_when_it_gets_enough_text()
    {
        var reading = Reader().Read(new PrefixExtractor("text:"), "text:conteúdo completo", PageUrl);

        Assert.Equal("conteúdo completo", reading.Result.Text);
        Assert.False(reading.UsedFallback);
    }

    [Fact]
    public void Falls_back_when_the_module_gets_no_text()
    {
        var reading = Reader().Read(new PrefixExtractor("text:"), "fallback:texto alternativo", PageUrl);

        Assert.Equal("texto alternativo", reading.Result.Text);
        Assert.True(reading.UsedFallback);
    }

    [Fact]
    public void Falls_back_when_the_module_extractor_throws()
    {
        var reading = Reader().Read(new ThrowingExtractor(), "fallback:texto alternativo", PageUrl);

        Assert.True(reading.UsedFallback);
    }

    [Fact]
    public void Keeps_a_short_module_text_when_the_fallback_fails()
    {
        var reading = Reader(minExtractedChars: 100).Read(new PrefixExtractor("text:"), "text:nota curta", PageUrl);

        Assert.Equal("nota curta", reading.Result.Text);
        Assert.False(reading.UsedFallback);
    }

    [Fact]
    public void Fails_when_no_extractor_gets_text()
    {
        var reading = Reader().Read(new PrefixExtractor("text:"), "<html></html>", PageUrl);

        Assert.False(reading.Result.IsSuccess);
        Assert.False(reading.UsedFallback);
    }

    [Fact]
    public void Warns_about_a_slow_extraction()
    {
        Reader().Read(new SlowExtractor(_clock, TimeSpan.FromSeconds(3)), "text:conteúdo completo", PageUrl);

        var warning = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(nameof(SlowExtractor), warning.Message, StringComparison.Ordinal);
        Assert.Contains(PageUrl.ToString(), warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_warn_about_a_fast_extraction()
    {
        Reader().Read(new SlowExtractor(_clock, TimeSpan.FromMilliseconds(1999)), "text:conteúdo completo", PageUrl);

        Assert.Empty(_logger.Entries);
    }

    private ArticleContentReader Reader(int minExtractedChars = 10) =>
        new(
            new PrefixExtractor("fallback:"),
            Options.Create(new CollectionOptions { MinExtractedChars = minExtractedChars }),
            _clock,
            _logger);

    /// <summary>Reads like <see cref="PrefixExtractor"/>, taking the given time on the fake clock.</summary>
    private sealed class SlowExtractor(FakeTimeProvider clock, TimeSpan duration) : IArticleContentExtractor
    {
        public ExtractionResult Extract(string html, Uri pageUrl)
        {
            clock.Advance(duration);
            return new PrefixExtractor().Extract(html, pageUrl);
        }
    }
}

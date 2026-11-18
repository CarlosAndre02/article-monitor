using ArticleMonitor.Domain.Sources;

namespace ArticleMonitor.UnitTests.Domain;

public class SourceTests
{
    private static readonly DateTimeOffset RunStart = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Completing_a_run_records_its_start_state_and_watermark()
    {
        var source = Source.Register(SourceKey.From("g1"));

        source.CompleteRun(RunStart, RunStart.AddHours(-1), "{\"etag\":1}");

        Assert.Equal(RunStart, source.LastCompletedRunStartedAt);
        Assert.Equal(RunStart.AddHours(-1), source.Watermark);
        Assert.Equal("{\"etag\":1}", source.DiscoveryState);
    }

    [Fact]
    public void The_watermark_only_moves_forward()
    {
        var source = Source.Register(SourceKey.From("g1"));
        source.CompleteRun(RunStart, RunStart.AddHours(-1), discoveryState: null);

        source.CompleteRun(RunStart.AddHours(4), RunStart.AddDays(-3), discoveryState: null);
        source.CompleteRun(RunStart.AddHours(8), newestPublishedAt: null, discoveryState: null);

        Assert.Equal(RunStart.AddHours(-1), source.Watermark);
        Assert.Equal(RunStart.AddHours(8), source.LastCompletedRunStartedAt);
    }

    [Fact]
    public void A_failed_run_keeps_watermark_and_state()
    {
        var source = Source.Register(SourceKey.From("g1"));
        source.CompleteRun(RunStart, RunStart.AddHours(-1), "state");

        source.RecordFailedRun(RunStart.AddHours(4));

        Assert.Equal(RunStart.AddHours(4), source.LastCompletedRunStartedAt);
        Assert.Equal(RunStart.AddHours(-1), source.Watermark);
        Assert.Equal("state", source.DiscoveryState);
    }

    [Theory]
    [InlineData(" G1 ", "g1")]
    [InlineData("cnn-brasil", "cnn-brasil")]
    public void Keys_are_normalized(string raw, string expected) => Assert.Equal(expected, SourceKey.From(raw).Value);

    [Theory]
    [InlineData("cnn brasil")]
    [InlineData("cnn--brasil")]
    [InlineData("-g1")]
    [InlineData("g1/")]
    public void Keys_reject_invalid_characters(string raw) =>
        Assert.Throws<ArgumentException>(() => SourceKey.From(raw));
}

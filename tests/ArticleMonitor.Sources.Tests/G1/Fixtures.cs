namespace ArticleMonitor.Sources.Tests.G1;

internal static class Fixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "G1", "Fixtures", name));

    /// <summary>Fixture of a section feed, named after the last segment of its URL (e.g. "politica.xml").</summary>
    public static string FeedFor(Uri feedUrl) => Read(SectionOf(feedUrl) + ".xml");

    public static string SectionOf(Uri feedUrl) => feedUrl.Segments[^1].TrimEnd('/');
}

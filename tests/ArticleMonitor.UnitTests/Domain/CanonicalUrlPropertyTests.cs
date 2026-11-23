using ArticleMonitor.Domain.Articles;
using CsCheck;

namespace ArticleMonitor.UnitTests.Domain;

/// <summary>Deduplication depends on these rules for any URL, not only for the examples in CanonicalUrlTests.</summary>
public class CanonicalUrlPropertyTests
{
    private const long Iterations = 2_000;

    private static readonly Gen<string> Word = Gen.String[Gen.Char["abcdefghijklmnopqrstuvwxyz0123456789"], 1, 8];

    private static readonly Gen<string> Host = Word.Select(word => $"{word}.example");

    private static readonly Gen<string> PathSegments = Word.Array[1, 4].Select(segments => string.Join('/', segments));

    // The "p" prefix keeps generated names from being tracking parameters.
    private static readonly Gen<string[]> Parameters =
        Gen.Select(Word, Word, (name, value) => $"p{name}={value}").Array[0, 5];

    private static readonly string[] TrackingNames = ["utm_source", "utm_medium", "UTM_Campaign", "fbclid", "GCLID"];

    private static readonly Gen<string> TrackingParameter = Gen.Select(
        Gen.Int[0, TrackingNames.Length - 1],
        Word,
        (index, value) => $"{TrackingNames[index]}={value}");

    private static readonly Gen<string> MessyUrl = Gen.String[Gen.Char["abcABC019-._~%/?#&=+ çãé:@"], 0, 40]
        .Select(tail => $"https://news.example/{tail}");

    [Fact]
    public void Normalizing_twice_gives_the_same_url() =>
        MessyUrl.Sample(
            raw =>
            {
                if (!CanonicalUrl.TryCreate(raw, out var url)) return;
                Assert.Equal(url.Value, Canonical(url.Value));
            },
            iter: Iterations);

    [Fact]
    public void Tracking_parameters_never_change_the_identity() =>
        Gen.Select(Host, PathSegments, Parameters, TrackingParameter.Array[1, 3]).Sample(
            (host, path, parameters, tracking) =>
                Assert.Equal(
                    Canonical(Link(host, path, parameters)),
                    Canonical(Link(host, path, [.. tracking, .. parameters]))),
            iter: Iterations);

    [Fact]
    public void Parameter_order_never_changes_the_identity() =>
        Gen.Select(Host, PathSegments, Parameters).Sample(
            (host, path, parameters) =>
                Assert.Equal(
                    Canonical(Link(host, path, parameters)),
                    Canonical(Link(host, path, [.. parameters.Reverse()]))),
            iter: Iterations);

    [Fact]
    public void Host_case_fragment_and_outer_spaces_never_change_the_identity() =>
        Gen.Select(Host, PathSegments, Parameters, Word).Sample(
            (host, path, parameters, fragment) =>
            {
                var link = Link(host, path, parameters);
                var variant = $"  {Link(host.ToUpperInvariant(), path, parameters)}#{fragment}  ";

                Assert.Equal(Canonical(link), Canonical(variant));
            },
            iter: Iterations);

    [Fact]
    public void Different_paths_are_different_articles() =>
        Gen.Select(Host, Word, Word).Where((_, first, second) => first != second).Sample(
            (host, first, second) =>
                Assert.NotEqual(Canonical(Link(host, first, [])), Canonical(Link(host, second, []))),
            iter: Iterations);

    [Fact]
    public void Any_text_is_rejected_or_becomes_a_valid_identity() =>
        Gen.OneOf(Gen.String, MessyUrl).Sample(
            raw =>
            {
                if (!CanonicalUrl.TryCreate(raw, out var url)) return;

                Assert.True(url.Value.Length <= CanonicalUrl.MaxLength);
                Assert.Contains(url.ToUri().Scheme, new[] { Uri.UriSchemeHttp, Uri.UriSchemeHttps });
                Assert.DoesNotContain('#', url.Value);
            },
            iter: Iterations);

    private static string Link(string host, string path, string[] parameters) =>
        parameters.Length == 0
            ? $"https://{host}/{path}"
            : $"https://{host}/{path}?{string.Join('&', parameters)}";

    private static string Canonical(string raw)
    {
        Assert.True(CanonicalUrl.TryCreate(raw, out var url), raw);
        return url.Value;
    }
}

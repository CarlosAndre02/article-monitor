using System.IO.Compression;
using System.Text;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Sources;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArticleMonitor.Infrastructure.Persistence;

/// <summary>Stores article text Brotli-compressed: roughly a third of the plain UTF-8 size.</summary>
internal sealed class BrotliTextConverter() : ValueConverter<string, byte[]>(
    text => Compress(text),
    bytes => Decompress(bytes))
{
    public static byte[] Compress(string text)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal))
        {
            brotli.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    public static string Decompress(byte[] bytes)
    {
        using var brotli = new BrotliStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var reader = new StreamReader(brotli, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

internal static class ValueMappings
{
    public static readonly ValueConverter<UrlHash, byte[]> UrlHashConverter =
        new(hash => hash.ToArray(), bytes => UrlHash.FromBytes(bytes));

    public static readonly ValueComparer<UrlHash> UrlHashComparer =
        new((left, right) => Equals(left, right), hash => hash.GetHashCode(), hash => hash);

    public static readonly ValueConverter<SourceKey, string> SourceKeyConverter =
        new(key => key.Value, value => SourceKey.From(value));
}

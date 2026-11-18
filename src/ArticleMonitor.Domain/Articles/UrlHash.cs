using System.Security.Cryptography;
using System.Text;

namespace ArticleMonitor.Domain.Articles;

/// <summary>SHA-256 of a canonical URL: the identity used to deduplicate articles.</summary>
public sealed class UrlHash : IEquatable<UrlHash>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    private UrlHash(byte[] bytes) => _bytes = bytes;

    public static UrlHash Of(string canonicalUrl) => new(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalUrl)));

    public static UrlHash FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"A URL hash must have {Length} bytes.", nameof(bytes));
        }

        return new UrlHash((byte[])bytes.Clone());
    }

    public byte[] ToArray() => (byte[])_bytes.Clone();

    public bool Equals(UrlHash? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as UrlHash);

    public override int GetHashCode() => BitConverter.ToInt32(_bytes, 0);

    public override string ToString() => Convert.ToHexStringLower(_bytes);
}

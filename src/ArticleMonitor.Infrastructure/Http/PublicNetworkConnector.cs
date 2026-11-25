using System.Net;
using System.Net.Sockets;

namespace ArticleMonitor.Infrastructure.Http;

/// <summary>
/// Opens connections only to public addresses. Every URL the collector requests comes from outside (feeds, links,
/// redirects), so without this a source could point it at the machine itself or its network (SSRF).
/// The check runs on the address actually connected to, after DNS resolution, which also defeats DNS rebinding.
/// </summary>
internal static class PublicNetworkConnector
{
    // Special-purpose ranges (RFC 6890 and IANA registries) that no news site lives on.
    private static readonly IPNetwork[] NonPublicNetworks =
    [
        .. new[]
        {
            "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
            "192.0.0.0/24", "192.168.0.0/16", "198.18.0.0/15", "224.0.0.0/4", "240.0.0.0/4",
            "::/128", "::1/128", "fc00::/7", "fe80::/10", "ff00::/8",
        }.Select(IPNetwork.Parse),
    ];

    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        var publicAddresses = addresses.Where(IsPublic).ToArray();
        if (publicAddresses.Length == 0)
        {
            throw new HttpRequestException($"{endpoint.Host} has no public address; connection refused.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(publicAddresses, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return !NonPublicNetworks.Any(network => network.Contains(normalized));
    }
}

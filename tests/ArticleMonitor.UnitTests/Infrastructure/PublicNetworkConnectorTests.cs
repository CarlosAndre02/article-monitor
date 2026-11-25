using System.Net;
using ArticleMonitor.Infrastructure.Http;

namespace ArticleMonitor.UnitTests.Infrastructure;

public class PublicNetworkConnectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    public void Rejects_addresses_outside_the_public_internet(string address) =>
        Assert.False(PublicNetworkConnector.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("186.192.81.5")]
    [InlineData("2800:3f0:4001:80a::200e")]
    [InlineData("::ffff:8.8.8.8")]
    public void Accepts_public_addresses(string address) =>
        Assert.True(PublicNetworkConnector.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("https://127.0.0.1:9/")]
    [InlineData("https://localhost:9/")]
    public async Task Never_connects_to_the_local_machine(string url)
    {
        using var handler = new SocketsHttpHandler { ConnectCallback = PublicNetworkConnector.ConnectAsync };
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url, Ct));

        Assert.Contains("no public address", exception.ToString(), StringComparison.Ordinal);
    }
}

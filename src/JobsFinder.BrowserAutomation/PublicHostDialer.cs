using System.Net;
using System.Net.Sockets;

namespace JobsFinder.BrowserAutomation;

/// <summary>Connects to public addresses only. It connects to the address it checked, so DNS cannot change in between.</summary>
internal static class PublicHostDialer
{
    private const int HttpsPort = 443;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    // Everything that is not a public unicast address, including ranges that embed an IPv4 address.
    private static readonly IPNetwork[] NotPublic =
    [
        .. new[]
        {
            "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24",
            "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24",
            "224.0.0.0/4", "240.0.0.0/4",
            "::/96", "64:ff9b::/96", "64:ff9b:1::/48", "100::/64", "2001::/23", "2001:db8::/32", "2002::/16", "fc00::/7",
            "fe80::/10", "fec0::/10", "ff00::/8",
        }.Select(n => IPNetwork.Parse(n)),
    ];

    public static async Task<Socket> DialAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || !addresses.All(IsPublic))
        {
            throw new SocketException((int)SocketError.HostUnreachable);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, HttpsPort, timeout.Token);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return !NotPublic.Any(n => n.BaseAddress.AddressFamily == plain.AddressFamily && n.Contains(plain));
    }
}

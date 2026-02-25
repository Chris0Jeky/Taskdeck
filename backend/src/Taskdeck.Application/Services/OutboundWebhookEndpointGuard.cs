using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Taskdeck.Application.Services;

public static partial class OutboundWebhookEndpointGuard
{
    private static readonly string[] BlockedHostnameSuffixes =
    [
        ".local",
        ".internal",
        ".home.arpa",
        ".localhost",
        ".localtest.me"
    ];

    private static readonly string[] DynamicDnsRoots =
    [
        "nip.io",
        "xip.io",
        "sslip.io"
    ];

    public static async Task<bool> IsHostBlockedAsync(
        string host,
        bool allowLocalhostEndpoints,
        CancellationToken cancellationToken = default)
    {
        var addresses = await ResolveAllowedAddressesAsync(host, allowLocalhostEndpoints, cancellationToken);
        return addresses.Count == 0;
    }

    public static async Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(
        string host,
        bool allowLocalhostEndpoints,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return [];
        }

        var normalizedHost = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.Equals(normalizedHost, "localhost", StringComparison.Ordinal))
        {
            if (!allowLocalhostEndpoints)
            {
                return [];
            }

            return [IPAddress.Loopback, IPAddress.IPv6Loopback];
        }

        if (IsBlockedByHostnamePolicy(normalizedHost))
        {
            return [];
        }

        if (IPAddress.TryParse(normalizedHost, out var literalAddress))
        {
            return IsBlockedIpAddress(literalAddress) ? [] : [literalAddress];
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(normalizedHost)
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

            return addresses
                .Where(address => !IsBlockedIpAddress(address))
                .Distinct()
                .ToList();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (SocketException)
        {
            return [];
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    private static bool IsBlockedByHostnamePolicy(string host)
    {
        if (BlockedHostnameSuffixes.Any(suffix =>
                host.EndsWith(suffix, StringComparison.Ordinal)))
        {
            return true;
        }

        if (DynamicDnsRoots.Any(root =>
                host.Equals(root, StringComparison.Ordinal) ||
                host.EndsWith($".{root}", StringComparison.Ordinal)))
        {
            foreach (Match match in EmbeddedIpv4Regex().Matches(host))
            {
                var candidateIp = match.Value.Replace('-', '.');
                if (!IPAddress.TryParse(candidateIp, out var embeddedIp))
                {
                    continue;
                }

                if (IsBlockedIpAddress(embeddedIp))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsBlockedIpAddress(IPAddress ipAddress)
    {
        if (IPAddress.IsLoopback(ipAddress))
        {
            return true;
        }

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ipAddress.GetAddressBytes();
            var first = bytes[0];
            var second = bytes[1];

            return first == 0 ||
                   first == 10 ||
                   first == 127 ||
                   (first == 100 && second >= 64 && second <= 127) ||
                   (first == 169 && second == 254) ||
                   (first == 172 && second >= 16 && second <= 31) ||
                   (first == 192 && second == 168) ||
                   first >= 224;
        }

        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ipAddress.IsIPv4MappedToIPv6)
            {
                return IsBlockedIpAddress(ipAddress.MapToIPv4());
            }

            if (ipAddress.Equals(IPAddress.IPv6None) ||
                ipAddress.IsIPv6LinkLocal ||
                ipAddress.IsIPv6SiteLocal ||
                ipAddress.IsIPv6Multicast)
            {
                return true;
            }

            var bytes = ipAddress.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC; // RFC4193 unique local.
        }

        return false;
    }

    [GeneratedRegex(@"\d{1,3}(?:[.-]\d{1,3}){3}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedIpv4Regex();
}

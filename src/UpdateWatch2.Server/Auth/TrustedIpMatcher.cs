using System.Net;

namespace UpdateWatch2.Server.Auth;

/// <summary>
/// Checks whether an address falls inside the CIDR range configured via
/// UPDATEWATCH2_TRUSTEDIP, exempting it from brute-force lockout.
/// </summary>
public static class TrustedIpMatcher
{
    public static bool IsTrusted(string? cidr, IPAddress? remoteIp)
    {
        if (string.IsNullOrWhiteSpace(cidr) || remoteIp is null)
        {
            return false;
        }

        var parts = cidr.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var network))
        {
            return false;
        }

        var prefixLength = parts.Length == 2 && int.TryParse(parts[1], out var p)
            ? p
            : network.AddressFamily == remoteIp.AddressFamily ? (network.GetAddressBytes().Length * 8) : 0;

        if (network.AddressFamily != remoteIp.AddressFamily)
        {
            return false;
        }

        var networkBytes = network.GetAddressBytes();
        var addressBytes = remoteIp.GetAddressBytes();

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (networkBytes[i] != addressBytes[i])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)~(0xFF >> remainingBits);
        return (networkBytes[fullBytes] & mask) == (addressBytes[fullBytes] & mask);
    }
}

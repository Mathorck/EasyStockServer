using System.Net.Sockets;
using System.Net;

namespace EasyStock.Server.Configuration;

/// <summary>The client address used for limits, after the forwarded-headers middleware.</summary>
public static class ClientIp
{
    /// <summary>IPv4 (also when mapped into IPv6) as is; IPv6 by /64, since one home gets a whole /64.</summary>
    public static string Key(HttpContext ctx) => Key(ctx.Connection.RemoteIpAddress);

    public static string Key(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}

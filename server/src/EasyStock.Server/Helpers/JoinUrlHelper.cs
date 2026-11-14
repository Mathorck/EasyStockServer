namespace EasyStock.Server.Helpers;

/// <summary>
/// The rules the app applies to a scanned server QR code (EasyStock shared/.../server/JoinCode.kt), so the server
/// never prints a code the app refuses: <see cref="IsValidServerUrl"/> is a port of <c>isValidServerUrl</c>,
/// <see cref="NormalizeFingerprint"/> of <c>normalizeFingerprint</c>, and <c>fp</c> is only allowed on https.
/// </summary>
public static class JoinUrlHelper
{
    /// <summary>
    /// <c>http://</c> or <c>https://</c>, a host (name, IPv4 or <c>[IPv6]</c>), an optional port 1-65535 and an
    /// optional path. No user info, query or fragment, no space or control character. Checked on the raw text:
    /// <see cref="Uri"/> would normalise what the app refuses (upper-case scheme, leading zeros in the port).
    /// </summary>
    public static bool IsValidServerUrl(string url)
    {
        string rest;
        if (url.StartsWith("https://", StringComparison.Ordinal)) rest = url["https://".Length..];
        else if (url.StartsWith("http://", StringComparison.Ordinal)) rest = url["http://".Length..];
        else return false;
        if (rest.Any(ch => ch <= 0x20 || ch == 0x7f || "@?#\\".Contains(ch))) return false;

        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        string host;
        string? port;
        if (authority.StartsWith('['))
        {
            var end = authority.IndexOf(']');
            if (end < 0) return false;
            host = authority[1..end];
            var after = authority[(end + 1)..];
            if (after.Length == 0) port = null;
            else if (after.StartsWith(':')) port = after[1..];
            else return false;
            if (host.Length == 0 || host.Any(ch => ch != ':' && ch != '.' && !char.IsAsciiDigit(ch) && !(char.ToLowerInvariant(ch) is >= 'a' and <= 'f')))
                return false;
        }
        else
        {
            var colon = authority.IndexOf(':');
            host = colon < 0 ? authority : authority[..colon];
            port = colon < 0 ? null : authority[(colon + 1)..];
            if (host.Length == 0 || host.Length > 253) return false;
            if (host.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-' && ch != '.')) return false;
            if (host.StartsWith('.') || host.StartsWith('-') || host.Contains("..", StringComparison.Ordinal)) return false;
        }
        if (port is not null)
        {
            if (!int.TryParse(port, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var number))
                return false;
            if (number is < 1 or > 65535 || port != number.ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
        }
        return true;
    }

    /// <summary>
    /// A SHA-256 fingerprint as 64 lower-case hex characters. Accepts upper case and <c>:</c> separators (as openssl
    /// prints it: <c>AB:CD:...</c>). Null when it is not a SHA-256.
    /// </summary>
    public static string? NormalizeFingerprint(string raw)
    {
        var hex = raw;
        if (raw.Contains(':'))
        {
            var bytes = raw.Split(':');
            if (bytes.Any(b => b.Length != 2)) return null;
            hex = string.Concat(bytes);
        }
        if (hex.Length != 64 || !hex.All(char.IsAsciiHexDigit)) return null;
        return hex.ToLowerInvariant();
    }

    public static bool IsHttps(string url) => url.StartsWith("https://", StringComparison.Ordinal);
}

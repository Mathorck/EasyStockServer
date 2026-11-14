using EasyStock.Server.Configuration;
using Microsoft.Extensions.Options;
using QRCoder;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// The links in the QR codes (docs/04-server-implementation-plan.md, 1.1):
/// <c>easystock://{kind}?server=&lt;PUBLIC_URL&gt;&amp;token=&lt;token&gt;&amp;fp=&lt;hex&gt;</c>, <c>fp</c> only when
/// EASYSTOCK_CERT_FINGERPRINT is set.
/// </summary>
public sealed class QrContentHandler(IOptions<ServerOptions> options)
{
    public const string Setup = "setup";
    public const string Invite = "invite";
    public const string Guest = "guest";
    public const string Reset = "reset";

    public string Build(string kind, string token)
    {
        var o = options.Value;
        var link = $"easystock://{kind}?server={Uri.EscapeDataString(o.PublicUrl)}&token={Uri.EscapeDataString(token)}";
        if (o.CertFingerprint is { } fp)
            link += $"&fp={Uri.EscapeDataString(fp.Replace(":", "").Replace(" ", "").ToLowerInvariant())}";
        return link;
    }

    /// <summary>The QR code as text, for the console.</summary>
    public static string Ascii(string content)
    {
        using var data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.M);
        return new AsciiQRCode(data).GetGraphicSmall();
    }
}

using EasyStock.Server.Configuration;
using EasyStock.Server.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QRCoder;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// The links in the QR codes (docs/04-server-implementation-plan.md, 1.1):
/// <c>easystock://{kind}?server=&lt;PUBLIC_URL&gt;&amp;token=&lt;token&gt;&amp;fp=&lt;hex&gt;</c>, <c>fp</c> only when
/// EASYSTOCK_CERT_FINGERPRINT is set and the public URL is https: the app refuses a fingerprint on an http address.
/// The fingerprint is checked at startup (<see cref="ServerOptionsValidator"/>); one that is still not a SHA-256 here
/// is left out rather than printed.
/// </summary>
public sealed partial class QrContentHandler(IOptions<ServerOptions> options, ILogger<QrContentHandler>? log = null)
{
    public const string Setup = "setup";
    public const string Invite = "invite";
    public const string Guest = "guest";
    public const string Reset = "reset";

    private readonly ILogger _log = log ?? (ILogger)NullLogger.Instance;
    private int _warned;

    public string Build(string kind, string token)
    {
        var o = options.Value;
        var link = $"easystock://{kind}?server={Uri.EscapeDataString(o.PublicUrl)}&token={Uri.EscapeDataString(token)}";
        if (o.CertFingerprint is { } raw && JoinUrlHelper.NormalizeFingerprint(raw) is { } fp)
        {
            if (JoinUrlHelper.IsHttps(o.PublicUrl))
                link += $"&fp={fp}";
            else if (Interlocked.Exchange(ref _warned, 1) == 0)
                LogFingerprintOnHttp(_log);
        }
        return link;
    }

    /// <summary>The QR code as text, for the console.</summary>
    public static string Ascii(string content)
    {
        using var data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.M);
        return new AsciiQRCode(data).GetGraphicSmall();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "EASYSTOCK_CERT_FINGERPRINT is ignored: EASYSTOCK_PUBLIC_URL is http, and the app refuses a fingerprint on an http address")]
    private static partial void LogFingerprintOnHttp(ILogger log);
}

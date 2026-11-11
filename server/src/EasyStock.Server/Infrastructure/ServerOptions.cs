using System.Net;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Infrastructure;

/// <summary>Settings read from EASYSTOCK_* environment variables.</summary>
public sealed class ServerOptions
{
    public string Db { get; set; } = "";
    public string PublicUrl { get; set; } = "";
    public string DataDir { get; set; } = "/data";
    public string? CertFingerprint { get; set; }
    public string LogLevel { get; set; } = "Information";
    public IReadOnlyList<string> TrustedProxies { get; set; } = [];

    public static ServerOptions From(IConfiguration c) => new()
    {
        Db = c["EASYSTOCK_DB"] ?? "",
        PublicUrl = c["EASYSTOCK_PUBLIC_URL"] ?? "",
        DataDir = NonEmpty(c["EASYSTOCK_DATA_DIR"]) ?? "/data",
        CertFingerprint = NonEmpty(c["EASYSTOCK_CERT_FINGERPRINT"]),
        LogLevel = NonEmpty(c["EASYSTOCK_LOG_LEVEL"]) ?? "Information",
        TrustedProxies = (c["EASYSTOCK_TRUSTED_PROXIES"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed class ServerOptionsValidator : IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServerOptions o)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(o.Db))
            errors.Add("EASYSTOCK_DB is required (PostgreSQL connection string).");
        if (string.IsNullOrWhiteSpace(o.PublicUrl))
            errors.Add("EASYSTOCK_PUBLIC_URL is required.");
        else if (!Uri.TryCreate(o.PublicUrl, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            errors.Add("EASYSTOCK_PUBLIC_URL must be an absolute http(s) URL.");
        if (!Enum.TryParse<LogLevel>(o.LogLevel, ignoreCase: true, out _))
            errors.Add($"EASYSTOCK_LOG_LEVEL '{o.LogLevel}' is not a valid log level.");
        foreach (var p in o.TrustedProxies)
        {
            var ok = p.Contains('/') ? System.Net.IPNetwork.TryParse(p, out _) : IPAddress.TryParse(p, out _);
            if (!ok) errors.Add($"EASYSTOCK_TRUSTED_PROXIES entry '{p}' is not an IP address or CIDR range.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

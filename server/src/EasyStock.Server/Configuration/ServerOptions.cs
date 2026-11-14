using System.Net;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Configuration;

/// <summary>Settings read from EASYSTOCK_* environment variables.</summary>
public sealed class ServerOptions
{
    public string Db { get; set; } = "";
    public string PublicUrl { get; set; } = "";
    public string DataDir { get; set; } = "/data";
    public string? CertFingerprint { get; set; }
    /// <summary>Null when unset: the "Serilog" configuration section decides.</summary>
    public string? LogLevel { get; set; }
    public IReadOnlyList<string> TrustedProxies { get; set; } = [];
    /// <summary>Cap on the total size of stored photos, in bytes (EASYSTOCK_IMAGE_QUOTA_MB, default 2048 MB).</summary>
    public long ImageQuotaBytes { get; set; } = DefaultImageQuotaMb * 1024 * 1024;

    public const long DefaultImageQuotaMb = 2048;

    public const int DefaultCleanupIntervalMinutes = 60;

    /// <summary>Minutes between two runs of the background cleanup job.</summary>
    public int CleanupIntervalMinutes { get; set; } = DefaultCleanupIntervalMinutes;

    public static ServerOptions From(IConfiguration c) => new()
    {
        Db = c["EASYSTOCK_DB"] ?? "",
        PublicUrl = c["EASYSTOCK_PUBLIC_URL"] ?? "",
        DataDir = NonEmpty(c["EASYSTOCK_DATA_DIR"]) ?? "/data",
        CertFingerprint = NonEmpty(c["EASYSTOCK_CERT_FINGERPRINT"]),
        LogLevel = NonEmpty(c["EASYSTOCK_LOG_LEVEL"]),
        TrustedProxies = (c["EASYSTOCK_TRUSTED_PROXIES"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ImageQuotaBytes = (long.TryParse(NonEmpty(c["EASYSTOCK_IMAGE_QUOTA_MB"]), out var mb) ? mb : DefaultImageQuotaMb) * 1024 * 1024,
        // An unparsable value becomes 0, which the validator rejects.
        CleanupIntervalMinutes = NonEmpty(c["EASYSTOCK_CLEANUP_INTERVAL_MINUTES"]) is { } m
            ? (int.TryParse(m, out var n) ? n : 0)
            : DefaultCleanupIntervalMinutes,
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
        if (o.LogLevel is not null && !Logging.TryParseLevel(o.LogLevel, out _))
            errors.Add($"EASYSTOCK_LOG_LEVEL '{o.LogLevel}' is not a valid log level (use one of: {string.Join(", ", Logging.LevelNames)}).");
        if (o.ImageQuotaBytes <= 0)
            errors.Add("EASYSTOCK_IMAGE_QUOTA_MB must be a positive number of megabytes.");
        if (o.CleanupIntervalMinutes < 1)
            errors.Add("EASYSTOCK_CLEANUP_INTERVAL_MINUTES must be a whole number of minutes, at least 1.");
        foreach (var p in o.TrustedProxies)
        {
            if (p.Contains('/'))
            {
                if (!System.Net.IPNetwork.TryParse(p, out var network))
                    errors.Add($"EASYSTOCK_TRUSTED_PROXIES entry '{p}' is not an IP address or CIDR range.");
                else if (network.PrefixLength < MinPrefixLength(network.BaseAddress))
                    errors.Add($"EASYSTOCK_TRUSTED_PROXIES entry '{p}' is too broad: it would trust forwarded headers from a large part of the network " +
                        $"(the smallest prefix allowed is /{MinPrefixLength(network.BaseAddress)} for {(network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "IPv4" : "IPv6")}). List your proxy address or its subnet.");
            }
            else if (!IPAddress.TryParse(p, out var address))
            {
                errors.Add($"EASYSTOCK_TRUSTED_PROXIES entry '{p}' is not an IP address or CIDR range.");
            }
            else if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            {
                errors.Add($"EASYSTOCK_TRUSTED_PROXIES entry '{p}' is not a usable proxy address.");
            }
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    // A /0 (or a wide range) would make every client a trusted proxy, so X-Forwarded-For could be forged
    // to dodge the rate limits. /8 still covers the usual private ranges (10.0.0.0/8, 172.16.0.0/12).
    private static int MinPrefixLength(IPAddress address) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 8 : 32;
}

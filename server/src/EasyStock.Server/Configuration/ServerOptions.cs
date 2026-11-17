
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
        PublicUrl = (c["EASYSTOCK_PUBLIC_URL"] ?? "").Trim(),
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

    /// <summary>
    /// Whether <c>/openapi/v1.json</c> is served: EASYSTOCK_OPENAPI (true/false) when set, else only in Development.
    /// </summary>
    public static bool OpenApiEnabled(IConfiguration c, IHostEnvironment env) =>
        bool.TryParse(NonEmpty(c["EASYSTOCK_OPENAPI"]), out var on) ? on : env.IsDevelopment();

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

using System.Security.Cryptography;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Configurations;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class HardeningTests
{
    [Theory]
    [InlineData("Ana\u0000")]
    [InlineData("A\nna")]
    [InlineData("‮evil")]
    [InlineData("a​b")]
    public void Display_names_reject_control_and_format_characters(string name) =>
        Assert.Throws<ProblemException>(() => AccountsHelper.CleanDisplayName(name));

    [Fact]
    public void Display_names_keep_ordinary_unicode() =>
        Assert.Equal("Zoé 山田", AccountsHelper.CleanDisplayName("  Zoé 山田 "));

    [Fact]
    public void Unknown_user_kind_fails_closed()
    {
        Assert.Equal(UserKind.Guest, AuthColumns.KindFromWire("guest"));
        Assert.Equal(UserKind.Full, AuthColumns.KindFromWire("full"));
        Assert.Throws<InvalidOperationException>(() => AuthColumns.KindFromWire("admin"));
    }

    [Fact]
    public void Every_problem_type_has_a_title()
    {
        var slugs = typeof(ProblemTypesHelper).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(ProblemTypesHelper.ContentType))
            .Select(f => (string)f.GetRawConstantValue()!);
        foreach (var slug in slugs) Assert.NotEqual("Error", ProblemTypesHelper.DefaultTitle(slug));
    }

    private const string Sha256 = "ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89";

    private static string QrLink(string publicUrl, string? fingerprint) =>
        new QrContentHandler(Options.Create(new ServerOptions { PublicUrl = publicUrl, CertFingerprint = fingerprint }))
            .Build(QrContentHandler.Setup, "t");

    [Fact]
    public void Qr_fingerprint_is_normalised_on_https() =>
        Assert.EndsWith("&fp=" + Sha256.Replace(":", ""), QrLink("https://x", Sha256.ToUpperInvariant()));

    [Fact]
    public void Qr_has_no_fingerprint_on_http() =>
        Assert.DoesNotContain("fp=", QrLink("http://192.168.1.10:8080", Sha256));

    [Fact]
    public void Qr_leaves_out_a_fingerprint_that_is_not_a_sha256() =>
        Assert.DoesNotContain("fp=", QrLink("https://x", "AB:CD&token=evil#"));

    [Theory]
    [InlineData("https://stock.example.org")]
    [InlineData("https://stock.example.org/easystock")]
    [InlineData("http://192.168.1.10:8080")]
    [InlineData("http://[fd00::1]:8080/")]
    public void Public_url_the_app_accepts_passes_startup(string url) =>
        Assert.True(new ServerOptionsValidator().Validate(null, Valid(publicUrl: url)).Succeeded);

    [Theory]
    [InlineData("https://user:pass@stock.example.org")]
    [InlineData("https://stock.example.org/?a=b")]
    [InlineData("https://stock.example.org/#top")]
    [InlineData("HTTPS://stock.example.org")]
    [InlineData("ftp://stock.example.org")]
    [InlineData("https://stock_example.org")]
    [InlineData("https://stock.example.org:080")]
    [InlineData("https://stock.example.org:70000")]
    [InlineData("https://.example.org")]
    [InlineData("https://[fd00::1")]
    public void Public_url_the_app_refuses_fails_startup(string url)
    {
        var result = new ServerOptionsValidator().Validate(null, Valid(publicUrl: url));
        Assert.True(result.Failed);
        Assert.Contains("EASYSTOCK_PUBLIC_URL", result.FailureMessage);
    }

    [Theory]
    [InlineData(Sha256)]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void Sha256_fingerprint_passes_startup(string fp) =>
        Assert.True(new ServerOptionsValidator().Validate(null, Valid(fingerprint: fp)).Succeeded);

    [Theory]
    [InlineData("AB:CD")]
    [InlineData("abc:def")]
    [InlineData("zz" + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcd")]
    [InlineData("da39a3ee5e6b4b0d3255bfef95601890afd80709")] // SHA-1
    public void Other_fingerprint_fails_startup(string fp)
    {
        var result = new ServerOptionsValidator().Validate(null, Valid(fingerprint: fp));
        Assert.True(result.Failed);
        Assert.Contains("EASYSTOCK_CERT_FINGERPRINT", result.FailureMessage);
    }

    [Theory]
    [InlineData("2001:db8::/64")]
    [InlineData("fd00:1:2:3::/112")]
    [InlineData("fd00::/63")]
    [InlineData("fd12:3456::/32")]
    [InlineData("fe80::/32")]
    [InlineData("::ffff:10.0.0.0/104")]
    [InlineData("10.0.0.0/8")]
    [InlineData("2001:db8::1")]
    public void Narrow_trusted_proxy_passes_startup(string entry) =>
        Assert.True(new ServerOptionsValidator().Validate(null, Valid(proxies: [entry])).Succeeded);

    [Theory]
    [InlineData("2001:db8::/32")]
    [InlineData("2001:db8::/48")]
    [InlineData("2001:db8::/63")]
    [InlineData("fd00::/31")]
    [InlineData("fc00::/7")]
    [InlineData("fe80::/16")]
    [InlineData("::/64")]
    [InlineData("::ffff:0:0/96")]
    [InlineData("::ffff:0.0.0.0/100")]
    [InlineData("10.0.0.0/7")]
    public void Wide_trusted_proxy_fails_startup(string entry)
    {
        var result = new ServerOptionsValidator().Validate(null, Valid(proxies: [entry]));
        Assert.True(result.Failed);
        Assert.Contains("too broad", result.FailureMessage);
    }

    private static ServerOptions Valid(string publicUrl = "https://stock.example.org", string? fingerprint = null,
        IReadOnlyList<string>? proxies = null) => new()
    {
        Db = "Host=localhost;Database=x",
        PublicUrl = publicUrl,
        CertFingerprint = fingerprint,
        TrustedProxies = proxies ?? [],
    };

    private static SigningKeyHandler Handler(string dir) =>
        new(Options.Create(new ServerOptions { DataDir = dir }), NullLogger<SigningKeyHandler>.Instance);

    private static string TempDir() => Directory.CreateTempSubdirectory("sk").FullName;

    [Fact]
    public void Signing_key_is_created_and_reloaded()
    {
        var dir = TempDir();
        using var a = Handler(dir);
        var kid = a.Key.KeyId;
        Assert.Empty(Directory.GetFiles(Path.Combine(dir, "keys"), "*.tmp"));
        using var b = Handler(dir);
        Assert.Equal(kid, b.Key.KeyId);
    }

    [Fact]
    public void Signing_key_must_be_p256()
    {
        var dir = TempDir();
        using var h = Handler(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(h.KeyPath)!);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        File.WriteAllText(h.KeyPath, p384.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(h.KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Throws<InvalidOperationException>(() => h.Key);
    }

    [Fact]
    public void Signing_key_tightens_loose_file_mode()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        using var h = Handler(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(h.KeyPath)!);
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(h.KeyPath, k.ExportPkcs8PrivateKeyPem());
        File.SetUnixFileMode(h.KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        Assert.NotNull(h.Key);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(h.KeyPath));
    }
}

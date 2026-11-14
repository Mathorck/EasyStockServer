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

    [Fact]
    public void Qr_fingerprint_is_escaped()
    {
        var qr = new QrContentHandler(Options.Create(new ServerOptions { PublicUrl = "https://x", CertFingerprint = "AB:CD&token=evil#" }));
        var link = qr.Build(QrContentHandler.Setup, "t");
        Assert.EndsWith("&fp=abcd%26token%3Devil%23", link);
    }

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

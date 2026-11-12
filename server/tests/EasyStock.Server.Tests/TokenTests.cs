using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class TokenTests(PostgresFixture pg)
{
    private static JsonElement Part(string jwt, int index)
    {
        var part = jwt.Split('.')[index].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part))).RootElement.Clone();
    }

    [Fact]
    public async Task Access_token_is_an_es256_jwt_with_the_session_claims()
    {
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.CreateUserAsync();

        var header = Part(user.AccessToken, 0);
        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.False(string.IsNullOrEmpty(header.GetProperty("kid").GetString()));

        var claims = Part(user.AccessToken, 1);
        Assert.Equal(user.Id.ToString(), claims.GetProperty("sub").GetString());
        Assert.Equal("member", claims.GetProperty("role").GetString());
        Assert.True(Guid.TryParse(claims.GetProperty("sid").GetString(), out _));
        Assert.Equal("https://stock.example.org", claims.GetProperty("iss").GetString());
        Assert.Equal("easystock-app", claims.GetProperty("aud").GetString());
        Assert.Equal(15 * 60, claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64()), user.Tokens.AccessTokenExpiresAt);

        var refresh = Convert.FromBase64String(user.RefreshToken.Replace('-', '+').Replace('_', '/') + "=");
        Assert.Equal(32, refresh.Length);
    }

    [Fact]
    public void Signing_key_is_private_to_the_server_user()
    {
        using var factory = new ApiFactory(pg.ConnectionString);
        factory.CreateVersionedClient();
        var path = Path.Combine(factory.DataDir, "keys", "jwt-es256.pem");
        Assert.True(File.Exists(path));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal([path], Directory.GetFiles(Path.Combine(factory.DataDir, "keys")));
    }

    [Fact]
    public async Task Restart_keeps_the_signing_key_and_its_tokens()
    {
        var dataDir = Directory.CreateTempSubdirectory("easystock-tests-").FullName;
        try
        {
            TestUser user;
            using (var first = new ApiFactory(pg.ConnectionString, dataDir: dataDir))
                user = await first.CreateUserAsync();

            using var second = new ApiFactory(pg.ConnectionString, dataDir: dataDir);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await second.GetMeAsync(user.AccessToken)).StatusCode);
            Assert.DoesNotContain(second.Logs.Events, e => e.MessageTemplate.Text.StartsWith("Created a new token signing key", StringComparison.Ordinal));

            // kid = base64url(SHA-256(SubjectPublicKeyInfo)).
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(await File.ReadAllTextAsync(Path.Combine(dataDir, "keys", "jwt-es256.pem"), TestContext.Current.CancellationToken));
            var kid = Convert.ToBase64String(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(kid, Part(user.AccessToken, 0).GetProperty("kid").GetString());
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }
}

using EasyStock.Server.Configuration;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class ConfigurationUnitTests
{
    private static ServerOptions Options(string proxies) => new()
    {
        Db = "x", PublicUrl = "https://x.example", TrustedProxies = proxies.Split(',', StringSplitOptions.RemoveEmptyEntries),
    };

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("10.0.0.0/7")]
    [InlineData("2001:db8::/16")]
    [InlineData("0.0.0.0")]
    public void Overly_broad_trusted_proxies_are_rejected(string entry)
    {
        var result = new ServerOptionsValidator().Validate(null, Options(entry));
        Assert.True(result.Failed);
        Assert.Contains("EASYSTOCK_TRUSTED_PROXIES", result.FailureMessage);
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("172.16.0.0/12,192.168.1.5")]
    [InlineData("fd00::/32")]
    public void Reasonable_trusted_proxies_are_accepted(string entry) =>
        Assert.True(new ServerOptionsValidator().Validate(null, Options(entry)).Succeeded);

    [Theory]
    [InlineData(null, null, 8080)]
    [InlineData("9000", null, 9000)]
    [InlineData("9000;9001", "http://+:1234", 9000)]
    [InlineData(null, "https://+:443;http://*:5000", 5000)]
    [InlineData(null, "http://0.0.0.0:7000", 7000)]
    public void Health_probe_port_follows_the_listen_configuration(string? httpPorts, string? urls, int expected) =>
        Assert.Equal(expected, HealthProbe.ResolvePort(k => k switch
        {
            "ASPNETCORE_HTTP_PORTS" => httpPorts,
            "ASPNETCORE_URLS" => urls,
            _ => null,
        }));
}

public sealed class CompressionTests(PostgresFixture pg)
{
    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Json_answers_are_compressed_but_auth_answers_never_are()
    {
        using var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        var client = factory.CreateVersionedClient();

        var info = await GetAsync(client, "/api/v1/server-info");
        Assert.Contains("gzip", info.Content.Headers.ContentEncoding);

        var auth = await GetAsync(client, "/api/v1/auth/setup");
        Assert.Empty(auth.Content.Headers.ContentEncoding);
    }
}

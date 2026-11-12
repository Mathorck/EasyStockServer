using System.Net;
using System.Text.Json;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class ApiTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Health_is_200_when_database_is_reachable()
    {
        var response = await _factory.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ServerInfo_has_expected_shape_without_version_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _factory.CreateClient().GetAsync("/api/v1/server-info", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        foreach (var name in new[] { "serverName", "serverVersion", "apiVersion", "minAppVersion",
                     "supportedSchemaVersions", "setupDone", "hasData", "guestSignupEnabled" })
            Assert.True(root.TryGetProperty(name, out _), $"missing {name}");
        Assert.Equal("0.0.0", root.GetProperty("minAppVersion").GetString());
        Assert.Equal(10, root.GetProperty("supportedSchemaVersions")[0].GetInt32());
    }

    [Fact]
    public async Task Missing_version_headers_give_426_app_too_old()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _factory.CreateClient().GetAsync("/api/v1/anything", ct);
        Assert.Equal((HttpStatusCode)426, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("app-too-old", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Unsupported_schema_version_gives_426()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-EasyStock-App-Version", "1.0.0");
        client.DefaultRequestHeaders.Add("X-EasyStock-Schema-Version", "99");
        var response = await client.GetAsync("/api/v1/anything", TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)426, response.StatusCode);
    }

    [Fact]
    public async Task Valid_version_headers_reach_routing()
    {
        var response = await _factory.CreateVersionedClient().GetAsync("/api/v1/anything", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/api/v1/server-info")]
    public async Task Exempt_routes_do_not_need_headers(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

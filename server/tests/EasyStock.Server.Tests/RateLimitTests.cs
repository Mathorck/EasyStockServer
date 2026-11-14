using System.Net;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class RateLimitTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> Login(HttpClient client) =>
        client.PostJson("/api/v1/auth/login", new { username = AuthHelpers.UniqueName(), password = "wrong password", deviceName = "x" });

    private static HttpClient Forwarded(ApiFactory factory, string remoteIp, string forwardedFor)
    {
        var client = factory.CreateVersionedClient(remoteIp: remoteIp);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    [Fact]
    public async Task Auth_endpoints_allow_10_requests_per_minute_per_ip()
    {
        using var factory = new ApiFactory(pg.ConnectionString);
        var client = factory.CreateVersionedClient();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client)).StatusCode);

        var limited = await Login(client);
        var problem = await limited.Problem(HttpStatusCode.TooManyRequests, "rate-limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.True(problem.GetProperty("retryAfter").GetInt32() > 0);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.CreateVersionedClient())).StatusCode);
    }

    [Fact]
    public async Task Other_endpoints_allow_120_requests_per_minute_per_session()
    {
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.CreateUserAsync();
        // A new client address each time: the limit follows the session, not the address.
        for (var i = 0; i < 120; i++)
            Assert.Equal(HttpStatusCode.OK, (await factory.GetMeAsync(user.AccessToken)).StatusCode);
        await (await factory.GetMeAsync(user.AccessToken)).Problem(HttpStatusCode.TooManyRequests, "rate-limited");
    }

    [Fact]
    public async Task Forwarded_address_is_used_behind_a_loopback_proxy()
    {
        using var factory = new ApiFactory(pg.ConnectionString);
        var client = Forwarded(factory, "127.0.0.1", ApiFactory.RandomIp());
        for (var i = 0; i < 10; i++) await Login(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(client)).StatusCode);

        // Same proxy, another client behind it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(Forwarded(factory, "127.0.0.1", ApiFactory.RandomIp()))).StatusCode);
    }

    [Fact]
    public async Task Forwarded_header_from_an_untrusted_peer_is_ignored()
    {
        using var factory = new ApiFactory(pg.ConnectionString);
        var peer = ApiFactory.RandomIp();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(Forwarded(factory, peer, ApiFactory.RandomIp()))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(Forwarded(factory, peer, ApiFactory.RandomIp()))).StatusCode);
    }

    [Fact]
    public async Task Trusted_proxies_come_from_the_environment()
    {
        using var factory = new ApiFactory(pg.ConnectionString,
            settings: new Dictionary<string, string> { ["EASYSTOCK_TRUSTED_PROXIES"] = "10.250.0.0/16" });
        for (var i = 0; i < 12; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(Forwarded(factory, "10.250.3.4", ApiFactory.RandomIp()))).StatusCode);
    }

    [Fact]
    public async Task Forwarded_address_is_the_partition_key_behind_a_trusted_proxy()
    {
        using var factory = new ApiFactory(pg.ConnectionString,
            settings: new Dictionary<string, string> { ["EASYSTOCK_TRUSTED_PROXIES"] = "10.250.0.0/16" });
        var client = ApiFactory.RandomIp();
        // The same forwarded client, through different proxy addresses: one partition.
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(Forwarded(factory, $"10.250.3.{i + 1}", client))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(Forwarded(factory, "10.250.9.9", client))).StatusCode);

        // Another forwarded client behind the same proxy has its own budget.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(Forwarded(factory, "10.250.3.1", ApiFactory.RandomIp()))).StatusCode);
    }
}

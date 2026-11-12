using System.Net;
using EasyStock.Server.Models;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>Each test has its own empty database: setup is only possible on a server with no user.</summary>
public sealed class SetupTests(PostgresFixture pg)
{
    private static object Body(string setupToken, string username, string password = AuthHelpers.Password) =>
        new { setupToken, username, displayName = "Owner", password, deviceName = "Pixel 8" };

    [Fact]
    public async Task Setup_creates_the_first_admin_once()
    {
        using var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        var client = factory.CreateVersionedClient();
        var token = Assert.Single(factory.Logs.SetupTokens());

        await (await client.PostJson("/api/v1/auth/setup", Body("wrong-token", "owner"))).Problem(HttpStatusCode.NotFound, "invite-invalid");

        var answer = await (await client.PostJson("/api/v1/auth/setup", Body(token, "owner"))).Read<TokenAnswer>();
        Assert.Equal("admin", answer.User.Role);
        Assert.Equal("full", answer.User.Kind);
        Assert.Matches("^[0-9A-Z]{3}-[0-9A-Z]{3}$", answer.User.ShortCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.GetMeAsync(answer.AccessToken)).StatusCode);

        await (await client.PostJson("/api/v1/auth/setup", Body(token, "owner2"))).Problem(HttpStatusCode.Conflict, "setup-done");
        Assert.Equal(HttpStatusCode.OK, (await factory.LoginAsync("owner", AuthHelpers.Password)).StatusCode);
    }

    [Fact]
    public async Task Invalid_password_keeps_the_token_usable()
    {
        using var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        var client = factory.CreateVersionedClient();
        var token = Assert.Single(factory.Logs.SetupTokens());

        var problem = await (await client.PostJson("/api/v1/auth/setup", Body(token, "owner", "short")))
            .Problem(HttpStatusCode.BadRequest, "validation-failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("password", out _));
        await (await client.PostJson("/api/v1/auth/setup", Body(token, "x!"))).Problem(HttpStatusCode.BadRequest, "validation-failed");

        Assert.Equal(HttpStatusCode.OK, (await client.PostJson("/api/v1/auth/setup", Body(token, "owner"))).StatusCode);
    }

    [Fact]
    public async Task Restart_prints_a_new_token_and_the_old_one_stops_working()
    {
        var db = await pg.CreateDatabaseAsync();
        string first;
        using (var factory = new ApiFactory(db))
        {
            factory.CreateVersionedClient();
            first = Assert.Single(factory.Logs.SetupTokens());
        }

        using var restarted = new ApiFactory(db);
        var client = restarted.CreateVersionedClient();
        var second = Assert.Single(restarted.Logs.SetupTokens());
        Assert.NotEqual(first, second);

        await (await client.PostJson("/api/v1/auth/setup", Body(first, "owner"))).Problem(HttpStatusCode.NotFound, "invite-invalid");
        Assert.Equal(HttpStatusCode.OK, (await client.PostJson("/api/v1/auth/setup", Body(second, "owner"))).StatusCode);

        using var afterSetup = new ApiFactory(db);
        afterSetup.CreateVersionedClient();
        Assert.Empty(afterSetup.Logs.SetupTokens());
    }

    [Fact]
    public async Task Setup_log_line_has_a_qr_code_and_an_app_link()
    {
        using var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        factory.CreateVersionedClient();
        var line = Assert.Single(factory.Logs.Events, e => e.Properties.ContainsKey("SetupToken")).RenderMessage();
        Assert.Contains("easystock://setup?server=", line);
        Assert.Contains("█", line);
    }
}

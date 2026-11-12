using System.Net;
using System.Text.Json;
using EasyStock.Server.Enums;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class PermissionTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Permission_is_checked_against_the_role()
    {
        await (await _factory.CreateVersionedClient().GetAsync("/api/v1/test-probe/view-stock", Ct)).Problem(HttpStatusCode.Unauthorized, "unauthorized");

        var viewer = _factory.CreateVersionedClient((await _factory.CreateUserAsync(Role.Viewer)).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/test-probe/view-stock", Ct)).StatusCode);
        await (await viewer.GetAsync("/api/v1/test-probe/administer", Ct)).Problem(HttpStatusCode.Forbidden, "not-allowed");

        var admin = _factory.CreateVersionedClient((await _factory.CreateUserAsync(Role.Admin)).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/test-probe/administer", Ct)).StatusCode);
    }

    [Fact]
    public async Task Role_comes_from_the_database_not_the_token()
    {
        var user = await _factory.CreateUserAsync(Role.Admin);
        var client = _factory.CreateVersionedClient(user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/test-probe/administer", Ct)).StatusCode);

        await _factory.WithDb(db => db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, Role.Viewer), Ct));
        await (await client.GetAsync("/api/v1/test-probe/administer", Ct)).Problem(HttpStatusCode.Forbidden, "not-allowed");

        using var who = JsonDocument.Parse(await (await client.GetAsync("/api/v1/test-probe/whoami", Ct)).Content.ReadAsStringAsync(Ct));
        Assert.Equal("Viewer", who.RootElement.GetProperty("role").GetString());
        Assert.Equal(user.Id, who.RootElement.GetProperty("userId").GetGuid());
        Assert.False(who.RootElement.GetProperty("isGuest").GetBoolean());
    }
}

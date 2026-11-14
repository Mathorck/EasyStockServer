using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class PushRegisterTests(PostgresFixture pg)
{
    private const string Path = "/api/v1/push/register";
    private const string Endpoint = "https://ntfy.example.org/up-abc123";

    // Each test signs in its own user (own session ID), because the database is shared by the whole run.

    [Fact]
    public async Task Register_saves_the_endpoint_of_the_session()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.SignInAsync(Role.Member);

        var response = await factory.ClientFor(user).PostAsJsonAsync(Path, new PushRegisterRequest(Endpoint), ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = await Find(factory, user.SessionId, ct);
        Assert.NotNull(row);
        Assert.Equal(Endpoint, row.Endpoint);
        Assert.Equal(row.CreatedAt, row.UpdatedAt);
        Assert.True(row.CreatedAt > 0);
    }

    [Fact]
    public async Task Registering_twice_replaces_the_endpoint_and_keeps_one_row()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.SignInAsync(Role.Admin);
        var client = factory.ClientFor(user);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync(Path, new PushRegisterRequest(Endpoint), ct)).StatusCode);
        var second = "http://192.168.1.10:8081/up-second";
        factory.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync(Path, new PushRegisterRequest(second), ct)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var rows = await db.Set<PushRegistration>().Where(r => r.DeviceSessionId == user.SessionId).ToListAsync(ct);
        var row = Assert.Single(rows);
        Assert.Equal(second, row.Endpoint);
        Assert.True(row.UpdatedAt > row.CreatedAt);
    }

    [Fact]
    public async Task Delete_removes_the_endpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.SignInAsync(Role.Viewer);
        var client = factory.ClientFor(user);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync(Path, new PushRegisterRequest(Endpoint), ct)).StatusCode);
        Assert.NotNull(await Find(factory, user.SessionId, ct));

        var response = await client.DeleteAsync(Path, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await Find(factory, user.SessionId, ct));
    }

    [Fact]
    public async Task Delete_without_a_registration_is_still_no_content()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.SignInAsync(Role.Guest);

        var response = await factory.ClientFor(user).DeleteAsync(Path, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.Member)]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Guest)]
    public async Task Every_signed_in_role_may_register(Role role)
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.SignInAsync(role);

        var response = await factory.ClientFor(user).PostAsJsonAsync(Path, new PushRegisterRequest(Endpoint), ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ntfy.example.org/up-abc")]
    [InlineData("/relative/path")]
    [InlineData("ftp://ntfy.example.org/up")]
    [InlineData("https://")]
    [InlineData("https://ntfy.example.org/has space")]
    [InlineData("https://user:pw@ntfy.example.org/up")]
    [InlineData("http://user@192.168.1.10/up")]
    public async Task Invalid_endpoint_gives_400(string? endpoint)
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.SignInAsync(Role.Member);

        var response = await factory.ClientFor(user).PostAsJsonAsync(Path, new PushRegisterRequest(endpoint), ct);

        await AssertValidationProblemAsync(response, ct);
        Assert.Null(await Find(factory, user.SessionId, ct));
    }

    [Fact]
    public async Task Endpoint_longer_than_2048_characters_gives_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.SignInAsync(Role.Member);
        var tooLong = "https://ntfy.example.org/" + new string('a', 2048);

        var response = await factory.ClientFor(user).PostAsJsonAsync(Path, new PushRegisterRequest(tooLong), ct);

        await AssertValidationProblemAsync(response, ct);
    }

    [Fact]
    public async Task Guest_logout_soft_deletes_the_guest_and_drops_its_push_registration()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var guest = await factory.SignInAsync(Role.Guest);
        var client = factory.ClientFor(guest);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync(Path, new PushRegisterRequest(Endpoint), ct)).StatusCode);

        var logout = await client.PostAsync("/api/v1/auth/logout", null, ct);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await factory.WithDb(async db =>
        {
            Assert.NotNull(await db.Users.Where(u => u.Id == guest.UserId).Select(u => u.DeletedAt).SingleAsync(ct));
            Assert.NotNull(await db.DeviceSessions.Where(s => s.Id == guest.SessionId).Select(s => s.RevokedAt).SingleAsync(ct));
            Assert.False(await db.Set<PushRegistration>().AnyAsync(r => r.DeviceSessionId == guest.SessionId, ct));
        });
    }

    [Fact]
    public async Task Anonymous_register_and_delete_give_401()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var client = factory.ClientFor(null);

        var post = await client.PostAsJsonAsync(Path, new PushRegisterRequest(Endpoint), ct);
        var delete = await client.DeleteAsync(Path, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    private static async Task<PushRegistration?> Find(WebApplicationFactory<Program> factory, Guid sessionId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        return await db.Set<PushRegistration>().AsNoTracking().FirstOrDefaultAsync(r => r.DeviceSessionId == sessionId, ct);
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("validation-failed", doc.RootElement.GetProperty("type").GetString());
    }
}

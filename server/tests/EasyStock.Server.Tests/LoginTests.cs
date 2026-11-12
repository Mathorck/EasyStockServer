using System.Net;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class LoginTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Right_password_opens_a_session()
    {
        var user = await _factory.CreateUserAsync(Role.Viewer);
        var answer = await (await _factory.LoginAsync(user.Username.ToUpperInvariant(), user.Password)).Read<TokenAnswer>();
        Assert.Equal(user.Id, answer.User.Id);
        Assert.Equal("viewer", answer.User.Role);
        Assert.NotEqual(user.RefreshToken, answer.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(answer.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_give_the_same_answer()
    {
        var user = await _factory.CreateUserAsync();
        await (await _factory.LoginAsync(user.Username, "wrong password")).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        await (await _factory.LoginAsync(AuthHelpers.UniqueName(), "wrong password")).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
    }

    [Fact]
    public async Task Five_failures_lock_the_account_for_15_minutes()
    {
        var user = await _factory.CreateUserAsync();
        for (var i = 0; i < 4; i++)
            await (await _factory.LoginAsync(user.Username, "wrong password")).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");

        var locked = await (await _factory.LoginAsync(user.Username, "wrong password")).Problem(HttpStatusCode.Unauthorized, "account-locked");
        Assert.True(locked.GetProperty("retryAfter").GetInt32() > 0);
        await (await _factory.LoginAsync(user.Username, user.Password)).Problem(HttpStatusCode.Unauthorized, "account-locked");

        _factory.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password)).StatusCode);

        // A success resets the count.
        for (var i = 0; i < 4; i++)
            await (await _factory.LoginAsync(user.Username, "wrong password")).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password)).StatusCode);
    }

    [Fact]
    public async Task Disabled_account_cannot_log_in_refresh_or_call()
    {
        var user = await _factory.CreateUserAsync();
        await _factory.WithDb(db => db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken));

        await (await _factory.LoginAsync(user.Username, user.Password)).Problem(HttpStatusCode.Unauthorized, "account-disabled");
        await (await _factory.RefreshAsync(user.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "account-disabled");
        await (await _factory.GetMeAsync(user.AccessToken)).Problem(HttpStatusCode.Unauthorized, "account-disabled");
    }
}

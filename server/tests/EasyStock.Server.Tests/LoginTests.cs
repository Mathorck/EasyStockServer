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
        for (var i = 0; i < 5; i++)
            await (await _factory.LoginAsync(user.Username, "wrong password")).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");

        // Locked: even the right password is refused, with the same answer as an unknown user.
        var locked = await (await _factory.LoginAsync(user.Username, user.Password)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.False(locked.TryGetProperty("retryAfter", out _));

        _factory.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password)).StatusCode);

        // A success resets the count.
        for (var i = 0; i < 4; i++)
            await (await _factory.LoginAsync(user.Username, "wrong password")).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password)).StatusCode);
    }

    [Fact]
    public async Task Locked_wrong_and_unknown_answers_are_identical()
    {
        var user = await _factory.CreateUserAsync();
        for (var i = 0; i < 5; i++)
            await _factory.LoginAsync(user.Username, "wrong password");

        async Task<(HttpStatusCode, string, string)> Answer(string username, string password)
        {
            var response = await _factory.LoginAsync(username, password);
            var body = System.Text.Json.Nodes.JsonNode.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
            body.Remove("traceId"); // differs on every request
            return (response.StatusCode, response.Headers.RetryAfter?.ToString() ?? "", body.ToJsonString());
        }

        var other = await _factory.CreateUserAsync();
        var wrong = await Answer(other.Username, "wrong password");
        Assert.Equal(wrong, await Answer(user.Username, "wrong password"));
        Assert.Equal(wrong, await Answer(user.Username, user.Password));
        Assert.Equal(wrong, await Answer(AuthHelpers.UniqueName(), "wrong password"));
    }

    [Fact]
    public async Task Parallel_guesses_cannot_get_past_the_limit()
    {
        var user = await _factory.CreateUserAsync();
        // Each from its own address, so the per-IP limit stays out of the way.
        await Task.WhenAll(Enumerable.Range(0, 7).Select(_ => _factory.LoginAsync(user.Username, "wrong password")));

        // Judged one at a time: the fifth locks and the rest are refused uncounted. Without the row lock, all
        // seven are judged and counted, leaving the count at 2 after the lockout.
        int count = -1;
        DateTimeOffset? lockoutEnd = null;
        await _factory.WithDb(async db =>
        {
            var row = await db.Users.Where(u => u.Id == user.Id)
                .Select(u => new { u.AccessFailedCount, u.LockoutEnd }).SingleAsync(TestContext.Current.CancellationToken);
            (count, lockoutEnd) = (row.AccessFailedCount, row.LockoutEnd);
        });
        Assert.Equal(0, count);
        Assert.NotNull(lockoutEnd);
        await (await _factory.LoginAsync(user.Username, user.Password)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
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

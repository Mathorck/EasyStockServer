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
    public async Task Five_failures_lock_the_account_for_15_minutes_from_that_address()
    {
        var user = await _factory.CreateUserAsync();
        var ip = ApiFactory.RandomIp();
        for (var i = 0; i < 5; i++)
            await (await _factory.LoginAsync(user.Username, "wrong password", ip)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");

        // Locked: even the right password is refused, with the same answer as an unknown user.
        var locked = await (await _factory.LoginAsync(user.Username, user.Password, ip)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.False(locked.TryGetProperty("retryAfter", out _));

        _factory.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password, ip)).StatusCode);

        // A success resets the count (another address: the per-IP request limit does not follow the fake clock).
        var other = ApiFactory.RandomIp();
        for (var i = 0; i < 4; i++)
            await (await _factory.LoginAsync(user.Username, "wrong password", other)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password, other)).StatusCode);
        for (var i = 0; i < 4; i++)
            await (await _factory.LoginAsync(user.Username, "wrong password", other)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password, other)).StatusCode);
    }

    [Fact]
    public async Task A_lock_from_one_address_does_not_lock_the_owner_out_elsewhere()
    {
        var user = await _factory.CreateUserAsync();
        var attacker = ApiFactory.RandomIp();
        for (var i = 0; i < 5; i++)
            await _factory.LoginAsync(user.Username, "wrong password", attacker);
        await (await _factory.LoginAsync(user.Username, user.Password, attacker)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");

        // The owner, from their own address, still gets in.
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password, ApiFactory.RandomIp())).StatusCode);
        // An IPv6 client is one address per /64: changing the low bits does not get a new budget.
        var v6 = "2001:db8:1:2::";
        for (var i = 0; i < 5; i++)
            await _factory.LoginAsync(user.Username, "wrong password", v6 + (i + 1));
        await (await _factory.LoginAsync(user.Username, user.Password, v6 + "99")).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
    }

    [Fact]
    public async Task Guesses_from_many_addresses_lock_the_account_everywhere()
    {
        var user = await _factory.CreateUserAsync();
        // Four per address: no pair reaches its own limit.
        var max = EasyStock.Server.Helpers.AuthLimitsHelper.MaxFailedLoginsPerAccount;
        for (var i = 0; i < max; i += 4)
        {
            var ip = ApiFactory.RandomIp();
            for (var j = i; j < Math.Min(i + 4, max); j++)
                await _factory.LoginAsync(user.Username, "wrong password", ip);
        }
        await (await _factory.LoginAsync(user.Username, user.Password, ApiFactory.RandomIp())).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");

        _factory.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password, ApiFactory.RandomIp())).StatusCode);
    }

    [Fact]
    public async Task Locked_wrong_and_unknown_answers_are_identical()
    {
        var user = await _factory.CreateUserAsync();
        var ip = ApiFactory.RandomIp();
        for (var i = 0; i < 5; i++)
            await _factory.LoginAsync(user.Username, "wrong password", ip);

        async Task<(HttpStatusCode, string, string)> Answer(string username, string password)
        {
            var response = await _factory.LoginAsync(username, password, ip);
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
        var ip = ApiFactory.RandomIp();
        await Task.WhenAll(Enumerable.Range(0, 7).Select(_ => _factory.LoginAsync(user.Username, "wrong password", ip)));

        // Each attempt is counted before it is judged: the fifth locks and the rest are refused.
        await (await _factory.LoginAsync(user.Username, user.Password, ip)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        // Only that address is locked; the account's own failures are below its limit.
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, user.Password, ApiFactory.RandomIp())).StatusCode);
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

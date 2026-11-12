using System.Net;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class AdminInvitesTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> Create(string? accessToken, object body) =>
        _factory.CreateVersionedClient(accessToken).PostJson("/api/v1/admin/invites", body);

    private Task<HttpResponseMessage> List(string? accessToken) =>
        _factory.CreateVersionedClient(accessToken).GetAsync("/api/v1/admin/invites", Ct);

    private Task<HttpResponseMessage> Cancel(string? accessToken, Guid id) =>
        _factory.CreateVersionedClient(accessToken).DeleteAsync($"/api/v1/admin/invites/{id}", Ct);

    private Task<HttpResponseMessage> Redeem(string? token) =>
        _factory.CreateVersionedClient().PostJson("/api/v1/auth/invite/redeem",
            new { inviteToken = token, username = AuthHelpers.UniqueName(), displayName = "New", password = AuthHelpers.Password, deviceName = "Pixel" });

    /// <summary>The token inside an invite QR link (easystock://invite?server=…&amp;token=…).</summary>
    private static string TokenOf(string? qr) =>
        QueryHelpers.ParseQuery(new Uri(qr!).Query)["token"].ToString();

    [Fact]
    public async Task Created_invite_redeems_through_auth_and_cancelled_one_does_not()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);

        var created = await (await Create(admin.AccessToken, new { role = "member" })).Read<InviteAnswer>();
        Assert.StartsWith("easystock://invite?", created.Qr);
        Assert.Equal("member", created.Role);

        var cancelled = await (await Create(admin.AccessToken, new { role = "viewer" })).Read<InviteAnswer>();
        Assert.Equal(HttpStatusCode.NoContent, (await Cancel(admin.AccessToken, cancelled.Id)).StatusCode);

        var answer = await (await Redeem(TokenOf(created.Qr))).Read<TokenAnswer>();
        Assert.Equal("member", answer.User.Role);
        Assert.Equal("full", answer.User.Kind);

        await (await Redeem(TokenOf(cancelled.Qr))).Problem(HttpStatusCode.NotFound, "invite-invalid");
    }

    [Fact]
    public async Task List_shows_only_open_invites()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);

        // Expires before the list is read: created first, then the clock moves past its 7 days.
        await Create(admin.AccessToken, new { role = "viewer" });
        _factory.Time.Advance(AuthLimitsHelper.InviteLifetime + TimeSpan.FromMinutes(1));
        var fresh = await (await _factory.LoginAsync(admin.Username, AuthHelpers.Password)).Read<TokenAnswer>();

        var open = await (await Create(fresh.AccessToken, new { role = "viewer" })).Read<InviteAnswer>();
        var used = await (await Create(fresh.AccessToken, new { role = "member" })).Read<InviteAnswer>();
        await (await Redeem(TokenOf(used.Qr))).Read<TokenAnswer>();
        var cancelled = await (await Create(fresh.AccessToken, new { role = "admin" })).Read<InviteAnswer>();
        await Cancel(fresh.AccessToken, cancelled.Id);

        var listed = await (await List(fresh.AccessToken)).Read<List<InviteAnswer>>();
        var single = Assert.Single(listed);
        Assert.Equal(open.Id, single.Id);
        Assert.Equal("viewer", single.Role);
        Assert.Null(single.Qr);
    }

    [Theory]
    [InlineData("superuser")]
    [InlineData("Admin")]
    [InlineData(null)]
    public async Task Invite_role_must_be_admin_member_or_viewer(string? role)
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);
        var problem = await (await Create(admin.AccessToken, new { role })).Problem(HttpStatusCode.BadRequest, "validation-failed");
        // Model-state keys (null role) and manual ones ("role") may differ in case.
        Assert.Contains(problem.GetProperty("errors").EnumerateObject(), p => string.Equals(p.Name, "role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_guest_invite_is_a_role_not_allowed_conflict()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);
        await (await Create(admin.AccessToken, new { role = "guest" })).Problem(HttpStatusCode.Conflict, "role-not-allowed");
    }

    [Fact]
    public async Task Cancelling_an_unknown_or_already_cancelled_invite_is_not_found()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);

        await (await Cancel(admin.AccessToken, Guid.NewGuid())).Problem(HttpStatusCode.NotFound, "not-found");

        var created = await (await Create(admin.AccessToken, new { role = "viewer" })).Read<InviteAnswer>();
        Assert.Equal(HttpStatusCode.NoContent, (await Cancel(admin.AccessToken, created.Id)).StatusCode);
        await (await Cancel(admin.AccessToken, created.Id)).Problem(HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Only_administrators_manage_invites()
    {
        var member = await _factory.CreateUserAsync(Role.Member);
        var invite = await (await Create((await _factory.CreateUserAsync(Role.Admin)).AccessToken, new { role = "viewer" }))
            .Read<InviteAnswer>();

        await (await Create(member.AccessToken, new { role = "viewer" })).Problem(HttpStatusCode.Forbidden, "not-allowed");
        await (await List(member.AccessToken)).Problem(HttpStatusCode.Forbidden, "not-allowed");
        await (await Cancel(member.AccessToken, invite.Id)).Problem(HttpStatusCode.Forbidden, "not-allowed");

        await (await Create(null, new { role = "viewer" })).Problem(HttpStatusCode.Unauthorized, "unauthorized");
        await (await List(null)).Problem(HttpStatusCode.Unauthorized, "unauthorized");
    }
}

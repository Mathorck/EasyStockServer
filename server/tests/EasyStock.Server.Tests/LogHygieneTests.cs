using System.Net.Http.Json;
using System.Net;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class LogHygieneTests(PostgresFixture pg)
{
    private const string DisplayName = "Zéphyrine Quarantenaire";

    [Fact]
    public async Task Logs_never_contain_tokens_passwords_or_display_names()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(await pg.CreateDatabaseAsync(),
            settings: new Dictionary<string, string> { ["EASYSTOCK_LOG_LEVEL"] = "Debug" });
        var client = factory.CreateVersionedClient();
        var setupToken = Assert.Single(factory.Logs.SetupTokens());
        var secrets = new List<string> { AuthHelpers.Password, "second password!", DisplayName };

        var admin = await (await client.PostJson("/api/v1/auth/setup",
            new { setupToken, username = "owner", displayName = DisplayName, password = AuthHelpers.Password, deviceName = "Pixel" })).Read<TokenAnswer>();
        secrets.AddRange([admin.AccessToken, admin.RefreshToken]);

        var invite = await factory.CreateInviteAsync(Role.Member);
        secrets.Add(invite.Token!);
        var member = await (await client.PostJson("/api/v1/auth/invite/redeem",
            new { inviteToken = invite.Token, username = "member", displayName = DisplayName, password = AuthHelpers.Password, deviceName = "Pixel" }))
            .Read<TokenAnswer>();
        secrets.AddRange([member.AccessToken, member.RefreshToken]);

        await factory.LoginAsync("member", "wrong password");
        var login = await (await factory.LoginAsync("member", AuthHelpers.Password)).Read<TokenAnswer>();
        var refreshed = await (await factory.RefreshAsync(login.RefreshToken)).Read<TokenAnswer>();
        await factory.RefreshAsync(login.RefreshToken); // reuse
        secrets.AddRange([login.AccessToken, login.RefreshToken, refreshed.AccessToken, refreshed.RefreshToken]);

        var reset = await factory.WithService<QrGrant, PasswordResetHandler>(s => s.CreateAsync(member.User.Id, admin.User.Id, ct));
        secrets.Add(reset.Token!);
        await factory.CreateVersionedClient().PostJson("/api/v1/auth/reset", new { resetToken = reset.Token, password = "second password!", deviceName = "x" });

        var publicToken = await factory.EnableGuestSignupAsync();
        secrets.Add(publicToken);
        var guest = await (await factory.CreateVersionedClient().PostJson("/api/v1/auth/guest-signup",
            new { publicToken, displayName = DisplayName, deviceName = "x" })).Read<TokenAnswer>();
        secrets.AddRange([guest.AccessToken, guest.RefreshToken]);

        var me = factory.CreateVersionedClient(admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsJsonAsync("/api/v1/me/password",
            new { currentPassword = AuthHelpers.Password, newPassword = "second password!" }, ct)).StatusCode);

        var events = factory.Logs.Events;
        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.Level == Serilog.Events.LogEventLevel.Debug); // really scanning at Debug
        foreach (var e in events)
        {
            var text = LogCapture.Text(e);
            foreach (var secret in secrets)
                Assert.False(text.Contains(secret, StringComparison.Ordinal), $"log contains a secret: {e.MessageTemplate.Text}");
            if (!e.Properties.ContainsKey("SetupToken"))
                Assert.DoesNotContain(setupToken, text, StringComparison.Ordinal);
        }
    }
}

using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using EasyStock.Server.DB;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Server.Tests.Infrastructure;

/// <summary>A user created for a test, with the secrets the test may need again.</summary>
public sealed record TestUser(string Username, string Password, TokenAnswer Tokens)
{
    public Guid Id => Tokens.User.Id;
    public string AccessToken => Tokens.AccessToken;
    public string RefreshToken => Tokens.RefreshToken;
}

/// <summary>A signed-in caller of any role (Guests included), with its real user and device session rows.</summary>
public sealed record SignedInUser(Guid UserId, Guid SessionId, Role Role, bool IsGuest, string AccessToken);

public static class AuthHelpers
{
    public const string Password = "correct horse battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim GuestSignups = new(1, 1);

    public static string UniqueName(string prefix = "u") => $"{prefix}{Guid.NewGuid():N}"[..20];

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(path, body, Ct);

    public static async Task<T> Read<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    /// <summary>Asserts a Problem Details answer and returns its type.</summary>
    public static async Task<JsonElement> Problem(this HttpResponseMessage response, HttpStatusCode status, string type)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == status, $"expected {(int)status}, got {(int)response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var root = JsonDocument.Parse(body).RootElement.Clone();
        Assert.Equal(type, root.GetProperty("type").GetString());
        return root;
    }

    public static async Task<T> WithService<T, TService>(this ApiFactory factory, Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public static async Task WithDb(this ApiFactory factory, Func<EasyStockDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<EasyStockDbContext>());
    }

    public static Task<InviteAnswer> CreateInviteAsync(this ApiFactory factory, Role role) =>
        factory.WithService<InviteAnswer, InviteHandler>(s => s.CreateAsync(role, null, Ct));

    /// <summary>A full account created through an invite.</summary>
    public static async Task<TestUser> CreateUserAsync(this ApiFactory factory, Role role = Role.Member)
    {
        var invite = await factory.CreateInviteAsync(role);
        var username = UniqueName();
        var response = await factory.CreateVersionedClient().PostJson("/api/v1/auth/invite/redeem",
            new { inviteToken = invite.Token, username, displayName = "Test user", password = Password, deviceName = "Pixel" });
        return new TestUser(username, Password, await response.Read<TokenAnswer>());
    }

    public static async Task<HttpResponseMessage> LoginAsync(this ApiFactory factory, string username, string password) =>
        await factory.CreateVersionedClient().PostJson("/api/v1/auth/login", new { username, password, deviceName = "Pixel" });

    public static async Task<HttpResponseMessage> RefreshAsync(this ApiFactory factory, string refreshToken) =>
        await factory.CreateVersionedClient().PostJson("/api/v1/auth/refresh", new { refreshToken });

    public static async Task<HttpResponseMessage> GetMeAsync(this ApiFactory factory, string accessToken) =>
        await factory.CreateVersionedClient(accessToken).GetAsync("/api/v1/me", Ct);

    /// <summary>Turns the public QR code on (no Guest limit unless given) and returns its token.</summary>
    public static async Task<string> EnableGuestSignupAsync(this ApiFactory factory, int? guestLimit = null)
    {
        await factory.WithService<PublicInviteAnswer, PublicInviteHandler>(s => s.UpdateAsync(true, guestLimit, Ct));
        var answer = await factory.WithService<PublicInviteAnswer, PublicInviteHandler>(s => s.RegenerateAsync(Ct));
        return answer.Token!;
    }

    public static async Task<HttpResponseMessage> GuestSignupAsync(this ApiFactory factory, string publicToken, string? remoteIp = null) =>
        await factory.CreateVersionedClient(remoteIp: remoteIp).PostJson("/api/v1/auth/guest-signup",
            new { publicToken, displayName = "Visitor", deviceName = "Phone" });

    /// <summary>
    /// Signs in a new user of <paramref name="role"/>: a Guest through the public QR code, any other role through an
    /// invite. <paramref name="guestKind"/> makes a non-Guest role a Guest account (a Guest pending promotion).
    /// </summary>
    public static async Task<SignedInUser> SignInAsync(this ApiFactory factory, Role role, bool guestKind = false)
    {
        TokenAnswer tokens;
        if (role == Role.Guest || guestKind)
        {
            // Regenerating the one public token would break a signup running in another test on the same database.
            await GuestSignups.WaitAsync(Ct);
            try
            {
                var response = await factory.GuestSignupAsync(await factory.EnableGuestSignupAsync());
                tokens = await response.Read<TokenAnswer>();
            }
            finally
            {
                GuestSignups.Release();
            }
        }
        else
            tokens = (await factory.CreateUserAsync(role)).Tokens;

        var userId = tokens.User.Id;
        Guid sessionId = default;
        await factory.WithDb(async db =>
        {
            if (role != Role.Guest && guestKind)
                await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Role, role), Ct);
            sessionId = await db.DeviceSessions.Where(s => s.UserId == userId).Select(s => s.Id).SingleAsync(Ct);
        });
        return new SignedInUser(userId, sessionId, role, role == Role.Guest || guestKind, tokens.AccessToken);
    }

    /// <summary>A versioned client signed in as <paramref name="user"/> (anonymous when null).</summary>
    public static HttpClient ClientFor(this ApiFactory factory, SignedInUser? user) =>
        factory.CreateVersionedClient(user?.AccessToken);
}

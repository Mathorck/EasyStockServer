using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>ES256 access tokens: <c>sub</c>, <c>role</c>, <c>sid</c>, <c>iat</c>, <c>exp</c> (15 minutes).</summary>
public sealed class AccessTokenHandler(SigningKeyHandler keys, IOptions<ServerOptions> options, TimeProvider time)
{
    public const string SessionClaim = "sid";
    public const string RoleClaim = "role";

    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(AppUser user, Guid sessionId)
    {
        // JWT times are whole seconds: the answer gives the exact expiry the token carries.
        var now = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        var expires = now + AuthLimitsHelper.AccessTokenLifetime;
        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.PublicUrl,
            Audience = AuthLimitsHelper.Audience,
            IssuedAt = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [RoleClaim] = user.Role.ToWire(),
                [SessionClaim] = sessionId.ToString(),
            },
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.EcdsaSha256),
        });
        return (token, expires);
    }

    /// <summary>How incoming tokens are checked; lifetimes use the injected clock.</summary>
    public static TokenValidationParameters ValidationParameters(string issuer, SigningKeyHandler keys, TimeProvider time) => new()
    {
        ValidIssuer = issuer,
        ValidAudience = AuthLimitsHelper.Audience,
        IssuerSigningKeyResolver = (_, _, _, _) => [keys.Key],
        ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        LifetimeValidator = (notBefore, expires, _, p) =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (notBefore is { } nbf && nbf > now + p.ClockSkew)
                throw new SecurityTokenNotYetValidException("The token is not valid yet.");
            if (expires is not { } exp || exp < now - p.ClockSkew)
                throw new SecurityTokenExpiredException("The token has expired.") { Expires = expires ?? DateTime.MinValue };
            return true;
        },
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = RoleClaim,
    };
}

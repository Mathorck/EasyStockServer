
namespace EasyStock.Server.Models.Auth;

/// <summary>Answer of every call that opens or renews a session.</summary>
public sealed record TokenAnswer(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    SessionUser User)
{
    public override string ToString() => $"TokenAnswer(user={User.Id})";
}

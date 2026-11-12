using System.ComponentModel.DataAnnotations;
using EasyStock.Server.Helpers;

namespace EasyStock.Server.Models;

// Bodies of /api/v1/auth. Usernames and passwords are checked by Identity (validation-failed with an errors map),
// so the token is only consumed when everything is valid. ToString never shows a secret.

public sealed record SetupRequest(
    [Required] string SetupToken,
    [Required] string Username,
    [Required, StringLength(AuthLimitsHelper.DisplayNameMaxLength)] string DisplayName,
    [Required] string Password,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => $"SetupRequest(username={Username})";
}

public sealed record RedeemInviteRequest(
    [Required] string InviteToken,
    [Required] string Username,
    [Required, StringLength(AuthLimitsHelper.DisplayNameMaxLength)] string DisplayName,
    [Required] string Password,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => $"RedeemInviteRequest(username={Username})";
}

public sealed record GuestSignupRequest(
    [Required] string PublicToken,
    [Required, StringLength(AuthLimitsHelper.DisplayNameMaxLength)] string DisplayName,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => "GuestSignupRequest";
}

public sealed record LoginRequest(
    [Required] string Username,
    [Required] string Password,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => $"LoginRequest(username={Username})";
}

public sealed record RefreshRequest([Required] string RefreshToken)
{
    public override string ToString() => "RefreshRequest";
}

public sealed record ResetPasswordRequest(
    [Required] string ResetToken,
    [Required] string Password,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => "ResetPasswordRequest";
}

public sealed record CompletePromotionRequest([Required] string Username, [Required] string Password)
{
    public override string ToString() => $"CompletePromotionRequest(username={Username})";
}

/// <summary>Answer of every call that opens or renews a session.</summary>
public sealed record TokenAnswer(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    SessionUser User)
{
    public override string ToString() => $"TokenAnswer(user={User.Id})";
}

/// <summary>The user a session belongs to. <c>kind</c> is full or guest, <c>shortCode</c> is XXX-XXX.</summary>
public sealed record SessionUser(Guid Id, string DisplayName, string Role, string Kind, string ShortCode);

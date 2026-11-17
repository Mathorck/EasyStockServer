using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Auth;

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

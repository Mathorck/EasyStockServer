using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Auth;

public sealed record GuestSignupRequest(
    [Required] string PublicToken,
    [Required, StringLength(AuthLimitsHelper.DisplayNameMaxLength)] string DisplayName,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => "GuestSignupRequest";
}

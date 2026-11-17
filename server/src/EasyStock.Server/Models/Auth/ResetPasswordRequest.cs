using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Auth;

public sealed record ResetPasswordRequest(
    [Required] string ResetToken,
    [Required] string Password,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => "ResetPasswordRequest";
}

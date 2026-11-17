using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Auth;

public sealed record RedeemInviteRequest(
    [Required] string InviteToken,
    [Required] string Username,
    [Required, StringLength(AuthLimitsHelper.DisplayNameMaxLength)] string DisplayName,
    [Required] string Password,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => $"RedeemInviteRequest(username={Username})";
}

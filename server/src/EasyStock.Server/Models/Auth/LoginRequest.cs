using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Auth;

public sealed record LoginRequest(
    [Required] string Username,
    [Required] string Password,
    [Required, StringLength(AuthLimitsHelper.DeviceNameMaxLength)] string DeviceName)
{
    public override string ToString() => $"LoginRequest(username={Username})";
}

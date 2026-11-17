using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Me;

/// <summary><c>POST /me/password</c>.</summary>
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword)
{
    public override string ToString() => "ChangePasswordRequest";
}

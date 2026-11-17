using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models.Auth;

public sealed record RefreshRequest([Required] string RefreshToken)
{
    public override string ToString() => "RefreshRequest";
}

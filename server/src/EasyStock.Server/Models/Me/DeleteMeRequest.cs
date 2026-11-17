
namespace EasyStock.Server.Models.Me;

/// <summary><c>DELETE /me</c>: the current password; optional for an account without one (a Guest).</summary>
public sealed record DeleteMeRequest(string? Password)
{
    public override string ToString() => "DeleteMeRequest";
}

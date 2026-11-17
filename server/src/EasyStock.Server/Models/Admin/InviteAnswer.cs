
namespace EasyStock.Server.Models.Admin;

/// <summary>An open invite. <see cref="Token"/> and <see cref="Qr"/> only when it is created.</summary>
public sealed record InviteAnswer(
    Guid Id,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? Token = null,
    string? Qr = null)
{
    public override string ToString() => $"InviteAnswer(id={Id}, role={Role})";
}

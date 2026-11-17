
namespace EasyStock.Server.Models.Admin;

/// <summary>The printed Guest QR code. <see cref="Token"/> and <see cref="Qr"/> only right after a regenerate.</summary>
public sealed record PublicInviteAnswer(
    bool Enabled,
    int? GuestLimit,
    int GuestCount,
    DateTimeOffset? CreatedAt,
    string? Token = null,
    string? Qr = null)
{
    public override string ToString() => $"PublicInviteAnswer(enabled={Enabled}, guestLimit={GuestLimit})";
}

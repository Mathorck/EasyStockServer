using System.ComponentModel.DataAnnotations;

namespace EasyStock.Server.Models;

// Answers of the account services the Admin endpoints call (invites, public QR, resets). A token and its QR link
// are only known when they are created: the server keeps a hash.

/// <summary>A single-use token for a QR code (password reset).</summary>
public sealed record QrGrant(string? Token, string? Qr, DateTimeOffset? ExpiresAt)
{
    public override string ToString() => $"QrGrant(expiresAt={ExpiresAt})";
}

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

/// <summary>
/// <c>PUT /admin/public-invite</c>: the whole state of the printed Guest QR code. A missing <c>guestLimit</c>
/// means no limit.
/// </summary>
public sealed record UpdatePublicInviteRequest([Required] bool? Enabled, int? GuestLimit);

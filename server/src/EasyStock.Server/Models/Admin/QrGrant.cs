
namespace EasyStock.Server.Models.Admin;

// Answers of the account services the Admin endpoints call (invites, public QR, resets). A token and its QR link
// are only known when they are created: the server keeps a hash.

/// <summary>A single-use token for a QR code (password reset).</summary>
public sealed record QrGrant(string? Token, string? Qr, DateTimeOffset? ExpiresAt)
{
    public override string ToString() => $"QrGrant(expiresAt={ExpiresAt})";
}

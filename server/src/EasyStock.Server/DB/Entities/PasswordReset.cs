namespace EasyStock.Server.DB.Entities;

/// <summary>Table <c>password_reset</c>: single-use reset token created by an Admin, valid 24 hours.</summary>
public sealed class PasswordReset
{
    public Guid Id { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public Guid UserId { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

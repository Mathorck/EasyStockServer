using EasyStock.Server.Enums;

namespace EasyStock.Server.DB.Entities;

/// <summary>Table <c>invite</c>: single-use invitation for a full account with a role chosen by an Admin.</summary>
public sealed class Invite
{
    public Guid Id { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public Role Role { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

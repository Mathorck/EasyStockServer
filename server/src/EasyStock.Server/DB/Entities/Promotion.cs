using EasyStock.Server.Enums;

namespace EasyStock.Server.DB.Entities;

/// <summary>Table <c>promotion</c>: an Admin gives a Guest a role; it applies once the Guest picks a username and password.</summary>
public sealed class Promotion
{
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Role TargetRole { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Null while pending (at most one pending promotion per user).</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary><see cref="Completed"/>, <see cref="Cancelled"/> or <see cref="Expired"/>.</summary>
    public string? EndReason { get; set; }
}

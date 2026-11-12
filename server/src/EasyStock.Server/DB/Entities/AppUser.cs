using EasyStock.Server.Enums;
using Microsoft.AspNetCore.Identity;

namespace EasyStock.Server.DB.Entities;

/// <summary>
/// Table <c>users</c>: the Identity user, without email, phone or two-factor columns (ignored in the model).
/// Guests have no username and no password hash. Server-only timestamps are timestamptz (UTC).
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public Role Role { get; set; } = Role.Guest;
    public UserKind Kind { get; set; }

    /// <summary>6 characters without look-alikes, derived from the ID (Helpers/ShortCodeHelper.cs).</summary>
    public string ShortCode { get; set; } = "";

    /// <summary>Null: the role default (Admin and Member on, Viewer off, Guest never).</summary>
    public bool? AlertsEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Set by the database trigger on insert, and on update only when display_name or deleted_at changes.</summary>
    public long ServerSeq { get; set; }
}

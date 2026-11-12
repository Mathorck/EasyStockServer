using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EasyStock.Server.Models;

/// <summary>A live device session of a user, as shown in the user list.</summary>
public sealed record AdminUserDevice(Guid Id, string DeviceName, DateTimeOffset LastUsedAt);

/// <summary>One row of <c>GET /admin/users</c> (deleted users are not listed).</summary>
public sealed record AdminUserAnswer(
    Guid Id,
    string DisplayName,
    string? Username,
    string Role,
    string Kind,
    string ShortCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    bool Disabled,
    IReadOnlyList<AdminUserDevice> Devices,
    PendingPromotionAnswer? PendingPromotion);

/// <summary>
/// <c>PATCH /admin/users/{id}</c>: only the fields present change. <c>disabled: true</c> disables the user and
/// ends their sessions, <c>false</c> enables them again.
/// </summary>
public sealed class UpdateAdminUserRequest
{
    public string? Role { get; set; }
    public string? DisplayName { get; set; }
    public bool? Disabled { get; set; }
}

/// <summary><c>POST /admin/users/{id}/promote</c>: the role to give and the short code the Admin read on the Guest's phone.</summary>
public sealed record PromoteUserRequest([Required] string Role, [Required] string ShortCode);

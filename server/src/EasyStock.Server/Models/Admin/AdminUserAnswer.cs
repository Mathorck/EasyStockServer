
namespace EasyStock.Server.Models.Admin;

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

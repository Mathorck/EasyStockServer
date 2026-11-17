
namespace EasyStock.Server.Models.Me;

/// <summary><c>GET /me</c>.</summary>
public sealed record MeAnswer(
    Guid Id,
    string DisplayName,
    string Role,
    string Kind,
    string ShortCode,
    bool? AlertsEnabled,
    PendingPromotionAnswer? PendingPromotion);

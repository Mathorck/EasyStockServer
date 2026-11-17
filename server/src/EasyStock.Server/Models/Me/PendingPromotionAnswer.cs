
namespace EasyStock.Server.Models.Me;

/// <summary>A pending promotion: the role a Guest gets once they choose a username and password.</summary>
public sealed record PendingPromotionAnswer(string Role, DateTimeOffset ExpiresAt);

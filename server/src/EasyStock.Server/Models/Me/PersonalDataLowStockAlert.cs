namespace EasyStock.Server.Models.Me;

/// <summary>A low-stock alert a movement of the user caused. Stock in thousandths, at the moment it went below the threshold.</summary>
public sealed record PersonalDataLowStockAlert(Guid Id, Guid ArticleId, string? ArticleName, DateTimeOffset CrossedAt, long Stock);

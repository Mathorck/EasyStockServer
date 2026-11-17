namespace EasyStock.Server.Models.Me;

/// <summary>A movement the user made. Quantity in thousandths, as stored; reason as stored (consumption...).</summary>
public sealed record PersonalDataMovement(
    Guid Id, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt, Guid ArticleId, string? ArticleName, Guid? ItemId,
    Guid LocationId, string? LocationName, long Quantity, string Reason, string? Comment, Guid? DeviceId);

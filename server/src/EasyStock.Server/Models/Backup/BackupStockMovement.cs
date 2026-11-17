using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupStockMovement(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("article_id")] string ArticleId,
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("quantity")] long Quantity,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("occurred_at")] long OccurredAt,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("item_id")] string? ItemId = null,
    [property: JsonPropertyName("comment")] string? Comment = null,
    [property: JsonPropertyName("created_by")] string? CreatedBy = null,
    [property: JsonPropertyName("device_id")] string? DeviceId = null);

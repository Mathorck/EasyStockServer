using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupStockReviewLine(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("review_id")] string ReviewId,
    [property: JsonPropertyName("article_id")] string ArticleId,
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("expected_quantity")] long ExpectedQuantity,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("item_id")] string? ItemId = null,
    [property: JsonPropertyName("counted_quantity")] long? CountedQuantity = null,
    [property: JsonPropertyName("result")] string Result = "unchecked",
    [property: JsonPropertyName("found_location_id")] string? FoundLocationId = null,
    [property: JsonPropertyName("info_flagged")] bool InfoFlagged = false,
    [property: JsonPropertyName("note")] string? Note = null,
    [property: JsonPropertyName("info_fixed_at")] long? InfoFixedAt = null,
    [property: JsonPropertyName("pending_item"), JsonConverter(typeof(RawJsonConverter))] string? PendingItem = null,
    [property: JsonPropertyName("checked_at")] long? CheckedAt = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

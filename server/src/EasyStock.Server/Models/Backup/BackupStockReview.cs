using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupStockReview(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("started_at")] long StartedAt,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("status")] string Status = "open",
    [property: JsonPropertyName("finished_at")] long? FinishedAt = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("article_id")] string ArticleId,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("serial_number")] string? SerialNumber = null,
    [property: JsonPropertyName("status")] string Status = "available",
    [property: JsonPropertyName("location_id")] string? LocationId = null,
    [property: JsonPropertyName("attributes"), JsonConverter(typeof(RawJsonConverter))] string? Attributes = null,
    [property: JsonPropertyName("archived")] bool Archived = false,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

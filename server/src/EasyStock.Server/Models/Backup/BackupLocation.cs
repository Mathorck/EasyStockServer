using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupLocation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("parent_id")] string? ParentId = null,
    [property: JsonPropertyName("icon")] string? Icon = null,
    [property: JsonPropertyName("color")] string? Color = null,
    [property: JsonPropertyName("barcode")] string? Barcode = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

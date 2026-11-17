using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupCategory(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("name_template")] string? NameTemplate = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

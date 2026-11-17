using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupCategoryAttribute(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("attribute_id")] string AttributeId,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("required")] bool Required = false,
    [property: JsonPropertyName("sort_order")] int SortOrder = 0,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

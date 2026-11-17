using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupArticle(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("sku")] string? Sku = null,
    [property: JsonPropertyName("name_auto")] bool NameAuto = false,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("category_id")] string? CategoryId = null,
    [property: JsonPropertyName("tracking")] string Tracking = "quantity",
    [property: JsonPropertyName("unit")] string Unit = "pce",
    [property: JsonPropertyName("barcode")] string? Barcode = null,
    [property: JsonPropertyName("threshold")] long? Threshold = null,
    [property: JsonPropertyName("image_path")] string? ImagePath = null,
    [property: JsonPropertyName("attributes"), JsonConverter(typeof(RawJsonConverter))] string? Attributes = null,
    [property: JsonPropertyName("archived")] bool Archived = false,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null,
    [property: JsonPropertyName("order_url")] string? OrderUrl = null);

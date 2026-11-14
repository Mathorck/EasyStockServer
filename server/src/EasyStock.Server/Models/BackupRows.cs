using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Server.Models;

// Rows of the app's backup format (tables/<table>.json): one record per table, JSON names as in docs/schema.dbml.
// Ids are strings (UUID, lower case) exactly as in the file; they become Guid when written to the database.
// JSON columns (attributes, options, pending_item) are kept as raw JSON text. Defaults match the app's entities,
// so an older backup without a later column still reads. A missing required value (a parameter without a default)
// fails the read: BackupFormatHelper.Json sets RespectRequiredConstructorParameters, so no [JsonRequired] is needed.

/// <summary>Writes and reads a JSON value as its raw text (the database column is jsonb).</summary>
public sealed class RawJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value);
}

public sealed record BackupCategory(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("name_template")] string? NameTemplate = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

public sealed record BackupAttributeDefinition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("data_type")] string DataType,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("options"), JsonConverter(typeof(RawJsonConverter))] string? Options = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

public sealed record BackupCategoryAttribute(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("attribute_id")] string AttributeId,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("required")] bool Required = false,
    [property: JsonPropertyName("sort_order")] int SortOrder = 0,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

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
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

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

public sealed record BackupStockReview(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("started_at")] long StartedAt,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("status")] string Status = "open",
    [property: JsonPropertyName("finished_at")] long? FinishedAt = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

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

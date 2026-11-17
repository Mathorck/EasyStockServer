using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupAttributeDefinition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("data_type")] string DataType,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("options"), JsonConverter(typeof(RawJsonConverter))] string? Options = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null,
    [property: JsonPropertyName("flag_when_past")] bool FlagWhenPast = false,
    [property: JsonPropertyName("notify_when_past")] bool NotifyWhenPast = false);

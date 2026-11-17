using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Server.Converters;

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

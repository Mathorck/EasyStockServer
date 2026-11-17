using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Server.Converters;

/// <summary>Timestamps go out as ISO 8601 in UTC with a "Z" (2026-11-12T10:00:00Z), whatever their offset.</summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);
}

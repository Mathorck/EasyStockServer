using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EasyStock.Server.DB.Entities;

namespace EasyStock.Server.Helpers;

/// <summary>
/// Shared-table rows to and from the app's wire format (sync/SyncWire.kt): the entity's columns as camelCase
/// fields, times in epoch ms, quantities in thousandths, and the jsonb columns as nested JSON. Unknown fields are
/// ignored, a missing field takes the entity default, and <c>serverSeq</c>, <c>createdBy</c> and <c>deviceId</c>
/// are never read from the phone nor sent back in push answers.
/// </summary>
public static class PushRowCodecHelper
{
    private static readonly HashSet<string> JsonColumns = ["attributes", "options", "pendingItem"];
    private static readonly HashSet<string> ServerOnly = ["serverSeq", "createdBy", "deviceId"];

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { Modify } },
    };

    /// <summary>The entity of <paramref name="table"/> from a wire row, or null when it doesn't parse.</summary>
    public static object? TryRead(string table, JsonObject row)
    {
        var type = PushTablesHelper.EntityType(table);
        if (type is null) return null;
        foreach (var field in PushTablesHelper.RequiredFields(table))
            if (!row.ContainsKey(field)) return null;
        try
        {
            return row.Deserialize(type, Options);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The wire row of an entity, without the server-only fields.</summary>
    public static JsonObject Write(object entity)
    {
        var row = JsonSerializer.SerializeToNode(entity, entity.GetType(), Options)!.AsObject();
        foreach (var field in ServerOnly) row.Remove(field);
        return row;
    }

    /// <summary>The fields of <paramref name="after"/> that differ from <paramref name="before"/>, nulls included.</summary>
    public static JsonObject Diff(JsonObject before, JsonObject after)
    {
        var diff = new JsonObject();
        foreach (var (name, value) in after)
            if (!JsonNode.DeepEquals(before[name], value))
                diff[name] = value?.DeepClone();
        return diff;
    }

    /// <summary>Same content once both are on the wire (jsonb columns compared as JSON, not as text).</summary>
    public static bool SameContent(object a, object b) => JsonNode.DeepEquals(Write(a), Write(b));

    private static void Modify(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || info.Type.Namespace != typeof(Category).Namespace) return;
        foreach (var property in info.Properties)
        {
            if (property.PropertyType == typeof(string) && JsonColumns.Contains(property.Name))
                property.CustomConverter = RawJsonConverter.Instance;
            else if (ServerOnly.Contains(property.Name))
                property.Set = null; // never read from the phone
        }
    }

    /// <summary>A jsonb column: any JSON value on the wire, its raw text in the entity.</summary>
    private sealed class RawJsonConverter : JsonConverter<string>
    {
        public static readonly RawJsonConverter Instance = new();

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.GetRawText();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value);
    }
}

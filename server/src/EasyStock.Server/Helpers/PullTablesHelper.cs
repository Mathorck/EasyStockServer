using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Server.DB.Entities;

namespace EasyStock.Server.Helpers;

/// <summary>
/// The pulled shared tables and their wire rows. A wire row is the entity with camelCase names: each C# property is
/// a schema.dbml column, so its camelCase name is the app's <c>snakeToCamel(@SerialName)</c> (shared/.../sync/SyncWire.kt).
/// Every column is written, nulls included. Timestamps stay epoch ms and quantities bigint thousandths.
/// jsonb columns (kept as text in the entities) go out as JSON values, as the app decodes them into JsonObject/List.
/// </summary>
internal static class PullTablesHelper
{
    public const string Category = "category";
    public const string AttributeDefinition = "attribute_definition";
    public const string CategoryAttribute = "category_attribute";
    public const string Location = "location";
    public const string Article = "article";
    public const string Item = "item";
    public const string StockMovement = "stock_movement";
    public const string StockReview = "stock_review";
    public const string StockReviewLine = "stock_review_line";

    /// <summary>Movement fields only for roles with ViewHistory (wire names).</summary>
    public static readonly string[] MovementPrivateFields = ["createdBy", "deviceId", "comment"];

    /// <summary>Tables only for roles with ViewReviews.</summary>
    public static readonly string[] ReviewTables = [StockReview, StockReviewLine];

    // Web defaults: camelCase properties, nulls written.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // jsonb columns, by wire name.
    private static readonly Dictionary<Type, string[]> JsonColumns = new()
    {
        [typeof(AttributeDefinition)] = ["options"],
        [typeof(Article)] = ["attributes"],
        [typeof(Item)] = ["attributes"],
        [typeof(StockReviewLine)] = ["pendingItem"],
    };

    public static JsonObject ToWire(object entity)
    {
        var row = JsonSerializer.SerializeToNode(entity, entity.GetType(), Options)!.AsObject();
        foreach (var column in JsonColumns.GetValueOrDefault(entity.GetType()) ?? [])
            if (row[column] is JsonValue value && value.TryGetValue<string>(out var text))
                row[column] = JsonNode.Parse(text);
        return row;
    }
}

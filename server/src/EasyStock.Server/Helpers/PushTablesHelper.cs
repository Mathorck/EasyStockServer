using EasyStock.Server.DB.Entities;

namespace EasyStock.Server.Helpers;

/// <summary>The nine shared tables as the wire names them, in the order a push applies them.</summary>
public static class PushTablesHelper
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

    /// <summary>Dependency order: a row only references rows of the tables before it (or its own, for locations).</summary>
    public static readonly IReadOnlyList<string> PushOrder =
    [
        Category, AttributeDefinition, CategoryAttribute, Location, Article, Item, StockMovement, StockReview, StockReviewLine,
    ];

    public static Type? EntityType(string table) => table switch
    {
        Category => typeof(Category),
        AttributeDefinition => typeof(AttributeDefinition),
        CategoryAttribute => typeof(CategoryAttribute),
        Location => typeof(Location),
        Article => typeof(Article),
        Item => typeof(Item),
        StockMovement => typeof(StockMovement),
        StockReview => typeof(StockReview),
        StockReviewLine => typeof(StockReviewLine),
        _ => null,
    };

    /// <summary>Wire fields a pushed row must have, besides <c>id</c>. Other fields default like the entity.</summary>
    internal static IReadOnlyList<string> RequiredFields(string table) => table switch
    {
        Category => ["createdAt", "updatedAt", "name"],
        AttributeDefinition => ["createdAt", "updatedAt", "key", "label", "dataType"],
        CategoryAttribute => ["createdAt", "updatedAt", "categoryId", "attributeId"],
        Location => ["createdAt", "updatedAt", "name"],
        Article => ["createdAt", "updatedAt", "name"],
        Item => ["createdAt", "updatedAt", "articleId"],
        StockMovement => ["createdAt", "articleId", "locationId", "quantity", "reason", "occurredAt"],
        StockReview => ["createdAt", "updatedAt", "locationId", "startedAt"],
        StockReviewLine => ["createdAt", "updatedAt", "reviewId", "articleId", "locationId", "expectedQuantity"],
        _ => [],
    };
}

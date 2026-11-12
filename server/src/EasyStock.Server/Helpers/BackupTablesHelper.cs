using System.Text.Json;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Helpers;

public static class BackupTablesHelper
{
    /// <summary>Parent tables first (foreign keys), as in the app.</summary>
    public static readonly IReadOnlyList<BackupTable> All = Enum.GetValues<BackupTable>();

    public static string Name(this BackupTable t) => t switch
    {
        BackupTable.Category => "category",
        BackupTable.AttributeDefinition => "attribute_definition",
        BackupTable.CategoryAttribute => "category_attribute",
        BackupTable.Location => "location",
        BackupTable.Article => "article",
        BackupTable.Item => "item",
        BackupTable.StockMovement => "stock_movement",
        BackupTable.StockReview => "stock_review",
        BackupTable.StockReviewLine => "stock_review_line",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    public static string Entry(this BackupTable t) => $"tables/{t.Name()}.json";
}

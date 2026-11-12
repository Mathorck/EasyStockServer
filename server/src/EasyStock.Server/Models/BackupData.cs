using System.Text.Json;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Models;

/// <summary>Every row of every synced table, soft-deleted ones included.</summary>
public sealed record BackupData
{
    public IReadOnlyList<BackupCategory> Categories { get; init; } = [];
    public IReadOnlyList<BackupAttributeDefinition> AttributeDefinitions { get; init; } = [];
    public IReadOnlyList<BackupCategoryAttribute> CategoryAttributes { get; init; } = [];
    public IReadOnlyList<BackupLocation> Locations { get; init; } = [];
    public IReadOnlyList<BackupArticle> Articles { get; init; } = [];
    public IReadOnlyList<BackupItem> Items { get; init; } = [];
    public IReadOnlyList<BackupStockMovement> Movements { get; init; } = [];
    public IReadOnlyList<BackupStockReview> Reviews { get; init; } = [];
    public IReadOnlyList<BackupStockReviewLine> ReviewLines { get; init; } = [];

    public int Count(BackupTable t) => t switch
    {
        BackupTable.Category => Categories.Count,
        BackupTable.AttributeDefinition => AttributeDefinitions.Count,
        BackupTable.CategoryAttribute => CategoryAttributes.Count,
        BackupTable.Location => Locations.Count,
        BackupTable.Article => Articles.Count,
        BackupTable.Item => Items.Count,
        BackupTable.StockMovement => Movements.Count,
        BackupTable.StockReview => Reviews.Count,
        BackupTable.StockReviewLine => ReviewLines.Count,
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    public bool IsEmpty => BackupTablesHelper.All.All(t => Count(t) == 0);

    public Dictionary<string, int> PerTable() =>
        BackupTablesHelper.All.Where(t => Count(t) > 0).ToDictionary(t => t.Name(), Count);

    /// <summary>Photo paths of the articles, soft-deleted ones included.</summary>
    public HashSet<string> ImagePaths() =>
        Articles.Where(a => a.ImagePath is not null).Select(a => a.ImagePath!).ToHashSet(StringComparer.Ordinal);
}

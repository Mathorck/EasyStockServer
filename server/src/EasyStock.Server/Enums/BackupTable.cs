using System.Text.Json;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Enums;

public enum BackupTable
{
    Category,
    AttributeDefinition,
    CategoryAttribute,
    Location,
    Article,
    Item,
    StockMovement,
    StockReview,
    StockReviewLine,
}

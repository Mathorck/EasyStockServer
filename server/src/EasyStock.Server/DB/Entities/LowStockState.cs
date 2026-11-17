namespace EasyStock.Server.DB.Entities;

/// <summary>
/// Whether an article is at or below its threshold, to alert only when it crosses it. No row means "not low";
/// an article that can't alert (serial, archived, deleted, no threshold) has no row.
/// </summary>
public sealed class LowStockState
{
    public Guid ArticleId { get; set; }

    public bool IsLow { get; set; }
}

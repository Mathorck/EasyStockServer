using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Helpers;

/// <summary>Rebuilds <c>low_stock_state</c> from the data, after a change that bypasses sync push (a backup import).</summary>
public static class LowStockStateHelper
{
    /// <summary>
    /// Sets every article's state to what its stock is now, with the same rule as
    /// <see cref="Handlers.Sync.LowStockHandler"/> (quantity-tracked, not archived, not deleted, with a threshold, stock
    /// at or below it). Adds no <c>low_stock_alert</c>: an import restores data, it is not a live crossing. Without this,
    /// a replace (which empties the table) makes the next push alert again for an article that was already low, and a
    /// merge leaves the state of the merged articles stale. Call it inside the import transaction.
    /// </summary>
    public static async Task<int> RebuildAsync(EasyStockDbContext db, CancellationToken ct)
    {
        var low = await db.Set<Article>()
            .Where(a => a.Tracking == "quantity" && !a.Archived && a.DeletedAt == null && a.Threshold != null
                && (db.Set<StockMovement>().Where(m => m.ArticleId == a.Id).Sum(m => (long?)m.Quantity) ?? 0) <= a.Threshold)
            .Select(a => a.Id)
            .ToListAsync(ct);
        await db.Set<LowStockState>().ExecuteDeleteAsync(ct);
        db.Set<LowStockState>().AddRange(low.Select(id => new LowStockState { ArticleId = id, IsLow = true }));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return low.Count;
    }
}

using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Sync;

/// <summary>
/// Threshold crossings of the articles a push touched (docs/03-server-spec.md "Low-stock alerts"). Runs in the
/// push transaction, after every row is applied, so stock that dips and comes back within one batch never alerts.
/// </summary>
public sealed class LowStockHandler(EasyStockDbContext db, TimeProvider time)
{
    /// <summary>Updates low_stock_state and adds one low_stock_alert per article that just went low.</summary>
    public async Task<int> DetectAsync(IReadOnlyCollection<Guid> articleIds, Guid causedBy, CancellationToken ct)
    {
        if (articleIds.Count == 0) return 0;
        var articles = await db.Set<Article>()
            .Where(a => articleIds.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                Eligible = a.Tracking == "quantity" && !a.Archived && a.DeletedAt == null && a.Threshold != null,
                a.Threshold,
                Stock = db.Set<StockMovement>().Where(m => m.ArticleId == a.Id).Sum(m => (long?)m.Quantity) ?? 0,
            })
            .ToListAsync(ct);
        var states = await db.Set<LowStockState>().Where(s => articleIds.Contains(s.ArticleId)).ToDictionaryAsync(s => s.ArticleId, ct);
        var now = time.GetUtcNow();
        var alerts = 0;
        foreach (var a in articles)
        {
            var state = states.GetValueOrDefault(a.Id);
            if (a.Eligible && a.Stock <= a.Threshold)
            {
                if (state is { IsLow: true }) continue;
                if (state is null) db.Add(new LowStockState { ArticleId = a.Id, IsLow = true });
                else state.IsLow = true;
                db.Add(new LowStockAlert
                {
                    Id = Guid.CreateVersion7(now),
                    ArticleId = a.Id,
                    CrossedAt = now.ToUnixTimeMilliseconds(),
                    Stock = a.Stock,
                    CausedByUserId = causedBy,
                });
                alerts++;
            }
            else if (state is not null)
            {
                // Back above the threshold, or can no longer alert: missing means "not low".
                db.Remove(state);
            }
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return alerts;
    }
}

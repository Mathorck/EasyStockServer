using System.Data;
using System.Text.Json.Nodes;
using EasyStock.Server.Interfaces;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Sync;

/// <summary>
/// Builds one pull page (docs/04-server-implementation-plan.md, 3.1). Everything is read in one read-only
/// REPEATABLE READ transaction, without the sync lock: writers are serialised by the lock their trigger takes, so
/// the snapshot holds a prefix of the seq space and the cursor never skips a row.
/// <list type="number">
/// <item>Per pullable table (the 9 shared tables, low_stock_alert, users): <c>server_seq &gt; since ORDER BY
/// server_seq LIMIT limit + 1</c>.</item>
/// <item>Merge by seq in memory and keep the first <c>limit</c>; <c>next</c> is the seq of the last kept row
/// (<c>since</c> if none), <c>hasMore</c> whether more remained.</item>
/// <item>After the cut: alerts filtered per caller, movement authors changed in the window plus authors of the kept
/// movements (never a user no movement names), then
/// <see cref="PullGuestFilterHelper"/>.</item>
/// </list>
/// </summary>
public sealed partial class PullHandler(
    EasyStockDbContext db,
    IPullUserDirectoryHandler userDirectory,
    ILogger<PullHandler> logger)
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 2000;

    private const string Users = "users";
    private const string Alerts = "low_stock_alert";

    private readonly record struct Candidate(long Seq, string Table, object Row);

    /// <param name="epoch">
    /// The dataset epoch for the answer, read by the caller before this call (and before the snapshot): it only changes
    /// after an import commits, so at worst new rows carry the old epoch (the next request gets 409 and resets), never
    /// old rows the new one.
    /// </param>
    public async Task<SyncPullResponse> BuildAsync(ICurrentUser caller, long since, int limit, string epoch, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(since);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxLimit);

        var take = limit + 1;
        var candidates = new List<Candidate>(take * 4);
        List<Candidate> kept;
        List<SyncPullUser> users;
        bool receivesAlerts;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
        {
            // Must be the first statement of the transaction.
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);

            // Dependency order: a table's rows are listed in this order in the answer.
            await AddAsync<Category>(PullTablesHelper.Category);
            await AddAsync<AttributeDefinition>(PullTablesHelper.AttributeDefinition);
            await AddAsync<CategoryAttribute>(PullTablesHelper.CategoryAttribute);
            await AddAsync<Location>(PullTablesHelper.Location);
            await AddAsync<Article>(PullTablesHelper.Article);
            await AddAsync<Item>(PullTablesHelper.Item);
            await AddAsync<StockMovement>(PullTablesHelper.StockMovement);
            await AddAsync<StockReview>(PullTablesHelper.StockReview);
            await AddAsync<StockReviewLine>(PullTablesHelper.StockReviewLine);
            await AddAsync<LowStockAlert>(Alerts);
            foreach (var user in await userDirectory.ChangedSinceAsync(since, take, ct))
                candidates.Add(new Candidate(user.ServerSeq, Users, user));

            // One global sequence: seqs are unique across tables.
            candidates.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            kept = candidates.Count > limit ? candidates.GetRange(0, limit) : candidates;

            users = await UsersAsync(kept, ct);
            // Same snapshot as the alerts it filters.
            receivesAlerts = await ReceivesAlertsAsync(caller, kept.Exists(c => c.Table == Alerts), ct);
            await tx.CommitAsync(ct);
        }

        var hasMore = candidates.Count > limit;
        var next = kept.Count > 0 ? kept[^1].Seq : since;

        var rows = new Dictionary<string, List<JsonObject>>();
        var alerts = new List<SyncPullAlert>();
        foreach (var c in kept)
        {
            switch (c.Table)
            {
                case Users:
                    break;
                case Alerts:
                    var alert = (LowStockAlert)c.Row;
                    // The author of the batch that caused it is not a recipient.
                    if (receivesAlerts && alert.CausedByUserId != caller.UserId)
                        alerts.Add(new SyncPullAlert(alert.Id, alert.ArticleId, alert.CrossedAt, alert.Stock));
                    break;
                default:
                    if (!rows.TryGetValue(c.Table, out var list)) rows[c.Table] = list = [];
                    list.Add(PullTablesHelper.ToWire(c.Row));
                    break;
            }
        }

        var response = PullGuestFilterHelper.Apply(new SyncPullResponse(rows, users, alerts, next, hasMore, epoch), caller);
        LogPulled(logger, since, kept.Count, next, hasMore);
        return response;

        async Task AddAsync<T>(string table) where T : class
        {
            var found = await db.Set<T>().AsNoTracking()
                .Where(e => EF.Property<long>(e, nameof(Category.ServerSeq)) > since)
                .OrderBy(e => EF.Property<long>(e, nameof(Category.ServerSeq)))
                .Take(take)
                .Select(e => new { Seq = EF.Property<long>(e, nameof(Category.ServerSeq)), Row = e })
                .ToListAsync(ct);
            foreach (var f in found) candidates.Add(new Candidate(f.Seq, table, f.Row));
        }
    }

    /// <summary>Movement authors changed in the window, plus the authors of the kept movements (same snapshot).</summary>
    private async Task<List<SyncPullUser>> UsersAsync(List<Candidate> kept, CancellationToken ct)
    {
        var byId = new Dictionary<Guid, string>();
        foreach (var c in kept)
            if (c.Row is PullUserChange u) byId[u.Id] = u.DisplayName;

        var authors = kept.Select(c => (c.Row as StockMovement)?.CreatedBy)
            .OfType<Guid>().Where(id => !byId.ContainsKey(id)).ToHashSet();
        if (authors.Count > 0)
            foreach (var (id, name) in await userDirectory.NamesAsync(authors, ct))
                byId[id] = name;

        return byId.Select(kv => new SyncPullUser(kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Whether the caller receives low-stock alerts: one query for its <c>alerts_enabled</c>, only when the page
    /// holds alerts. A disabled or deleted user receives none.
    /// </summary>
    private async Task<bool> ReceivesAlertsAsync(ICurrentUser caller, bool pageHasAlerts, CancellationToken ct)
    {
        if (!pageHasAlerts || !caller.IsAuthenticated || caller.IsGuest) return false;
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == caller.UserId && u.DisabledAt == null && u.DeletedAt == null)
            .Select(u => new { u.AlertsEnabled })
            .FirstOrDefaultAsync(ct);
        return user is not null && AlertRecipientsHelper.IsRecipient(caller.Role, caller.IsGuest, user.AlertsEnabled);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pull since {Since}: {Count} rows, next {Next}, more {HasMore}")]
    private static partial void LogPulled(ILogger logger, long since, int count, long next, bool hasMore);
}

using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Server.Filters;
using EasyStock.Server.Interfaces;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Sync;

/// <summary>
/// Applies one push batch (docs/03-server-spec.md "Push processing", docs/04-server-implementation-plan.md
/// Phase 8): one transaction under the sync lock, idempotent by batch ID, rows in dependency order, each one
/// checked (role, values, references, first change wins by baseSeq), adjusted by the conflict rules of the app's BackupMerge, then saved under
/// its own savepoint. Then the low-stock detector, then the saved answer.
/// </summary>
public sealed partial class PushHandler(
    EasyStockDbContext db, ICurrentUser user, TimeProvider time, LowStockHandler detector, DatasetEpochHandler epochs,
    ILogger<PushHandler> log)
{
    public const int MaxRows = 5000;
    public const long MaxBodyBytes = 10 * 1024 * 1024;

    /// <summary>
    /// The answer exactly as sent and saved; null with <see cref="BatchIdReused"/> set, or with <see cref="ResetEpoch"/>
    /// (the current epoch) when the phone's dataset epoch is stale.
    /// </summary>
    public sealed record Outcome(string? Json, bool BatchIdReused = false, string? ResetEpoch = null);

    private static readonly JsonSerializerOptions ResponseOptions = new(JsonSerializerDefaults.Web);

    private sealed record Pending(JsonObject? Row, Guid? Id, string RawId);

    /// <summary>State shared by the rows of one batch.</summary>
    private sealed class BatchState
    {
        /// <summary>attribute_definition merged into another one with the same key: incoming ID → server ID.</summary>
        public Dictionary<Guid, Guid> AttributeAliases { get; } = [];

        /// <summary>Articles whose stock or threshold rules may have changed.</summary>
        public HashSet<Guid> TouchedArticles { get; } = [];
    }

    /// <param name="datasetEpoch">The phone's X-EasyStock-Dataset-Epoch header, or null.</param>
    public async Task<Outcome> PushAsync(
        Guid batchId, IReadOnlyDictionary<string, List<JsonNode?>> rows, string? datasetEpoch, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireSyncLockAsync(ct);

        // Checked again under the lock, from the database (one primary-key read): the cached epoch is only updated
        // after an import has committed and released the lock, so a push that waited on that import passed the cached
        // check. Before the replay lookup: sync_batch survives a replace, and an old batch must not get its old answer.
        if (!string.IsNullOrEmpty(datasetEpoch) && await epochs.ReadAsync(db, ct) is { } current
            && !DatasetEpochHandler.Accepts(datasetEpoch, current))
            return new Outcome(null, ResetEpoch: current);

        // Checked under the lock: two identical batches at once serialise and the second gets the saved answer.
        var saved = await db.Set<SyncBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.BatchId == batchId, ct);
        if (saved is not null)
        {
            if (saved.UserId != user.UserId)
            {
                LogBatchIdReused(batchId, user.UserId);
                return new Outcome(null, BatchIdReused: true);
            }
            LogReplayed(batchId, user.UserId);
            return new Outcome(saved.Response);
        }

        var state = new BatchState();
        var results = new List<SyncPushResult>();
        foreach (var table in PushTablesHelper.PushOrder)
        {
            if (!rows.TryGetValue(table, out var tableRows)) continue;
            foreach (var row in await OrderAsync(table, tableRows.Select(Parse).ToList(), ct))
                results.Add(await ProcessRowAsync(table, row, state, ct));
        }
        foreach (var (table, tableRows) in rows.Where(t => !PushTablesHelper.PushOrder.Contains(t.Key)))
            foreach (var row in tableRows.Select(Parse))
                results.Add(Rejected(table, row.RawId, SyncPushReasonHelper.Invalid, null));

        var alerts = await detector.DetectAsync(state.TouchedArticles, user.UserId, ct);

        var json = JsonSerializer.Serialize(new SyncPushResponse(results), ResponseOptions);
        db.Add(new SyncBatch
        {
            BatchId = batchId,
            UserId = user.UserId,
            DeviceSessionId = user.SessionId,
            ReceivedAt = time.GetUtcNow(),
            Response = json,
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        await tx.CommitAsync(ct);

        LogApplied(batchId, user.UserId, results.Count(r => r.Status == SyncPushStatusHelper.Accepted),
            results.Count(r => r.Status == SyncPushStatusHelper.Changed), results.Count(r => r.Status == SyncPushStatusHelper.Rejected), alerts);
        return new Outcome(json);
    }

    private static Pending Parse(JsonNode? node)
    {
        if (node is not JsonObject row) return new Pending(null, null, "");
        var raw = row["id"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        return new Pending(row, Guid.TryParse(raw, out var id) ? id : null, raw);
    }

    /// <summary>
    /// Rows that update a server row first, so a rename frees its old value before a new row asks for it. Then,
    /// for locations, parents before children within the batch.
    /// </summary>
    private async Task<IReadOnlyList<Pending>> OrderAsync(string table, List<Pending> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => r.Id is not null).Select(r => r.Id!.Value).Distinct().ToList();
        var existing = (await ExistingIdsAsync(table, ids, ct)).ToHashSet();
        var ordered = rows.Where(r => r.Id is { } id && existing.Contains(id))
            .Concat(rows.Where(r => r.Id is not { } id || !existing.Contains(id)))
            .ToList();
        return table == PushTablesHelper.Location ? ParentsFirst(ordered) : ordered;
    }

    private static List<Pending> ParentsFirst(List<Pending> rows)
    {
        var index = new Dictionary<Guid, int>();
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Id is { } id) index.TryAdd(id, i);
        var visited = new bool[rows.Count];
        var result = new List<Pending>(rows.Count);
        void Visit(int i)
        {
            if (visited[i]) return;
            visited[i] = true; // also stops on a cycle within the batch; the cycle rule handles it
            if (rows[i].Row?["parentId"] is JsonValue p && p.TryGetValue<string>(out var s) && Guid.TryParse(s, out var parent)
                && index.TryGetValue(parent, out var j))
                Visit(j);
            result.Add(rows[i]);
        }
        for (var i = 0; i < rows.Count; i++) Visit(i);
        return result;
    }

    private Task<List<Guid>> ExistingIdsAsync(string table, List<Guid> ids, CancellationToken ct) => table switch
    {
        PushTablesHelper.Category => Ids<Category>(ids, ct),
        PushTablesHelper.AttributeDefinition => Ids<AttributeDefinition>(ids, ct),
        PushTablesHelper.CategoryAttribute => Ids<CategoryAttribute>(ids, ct),
        PushTablesHelper.Location => Ids<Location>(ids, ct),
        PushTablesHelper.Article => Ids<Article>(ids, ct),
        PushTablesHelper.Item => Ids<Item>(ids, ct),
        PushTablesHelper.StockMovement => Ids<StockMovement>(ids, ct),
        PushTablesHelper.StockReview => Ids<StockReview>(ids, ct),
        PushTablesHelper.StockReviewLine => Ids<StockReviewLine>(ids, ct),
        _ => Task.FromResult(new List<Guid>()),
    };

    private Task<List<Guid>> Ids<T>(List<Guid> ids, CancellationToken ct) where T : class =>
        db.Set<T>().Where(e => ids.Contains(EF.Property<Guid>(e, "Id"))).Select(e => EF.Property<Guid>(e, "Id")).ToListAsync(ct);

    private async Task<SyncPushResult> ProcessRowAsync(string table, Pending pending, BatchState state, CancellationToken ct)
    {
        try
        {
            if (pending.Row is null || pending.Id is not { } id)
                return Rejected(table, pending.RawId, SyncPushReasonHelper.Invalid, null);
            var rawId = pending.RawId;
            var type = PushTablesHelper.EntityType(table)!;

            // Including soft-deleted rows: the push may restore one, and references to them are valid.
            var existing = await db.FindAsync(type, [id], ct);
            var incoming = PushRowCodecHelper.TryRead(table, pending.Row);
            if (incoming is null || !PushRowCodecHelper.TryReadBaseSeq(pending.Row, out var baseSeq))
                return Rejected(table, rawId, SyncPushReasonHelper.Invalid, existing);
            if (incoming is CategoryAttribute link && state.AttributeAliases.TryGetValue(link.AttributeId, out var mergedInto))
                link.AttributeId = mergedInto;

            // Movements never change: the same ID is the same movement.
            if (existing is StockMovement kept)
            {
                return SameMovement((StockMovement)incoming, kept)
                    ? Accepted(table, rawId, kept)
                    : Changed(table, rawId, SyncPushConflictHelper.MovementKept, PushRowCodecHelper.Write(kept), kept);
            }

            // An identical no-op is accepted without a write and without a seq bump, whatever the role and baseSeq.
            if (existing is not null && PushRowCodecHelper.SameContent(incoming, existing))
                return Accepted(table, rawId, existing);

            if (!PushPermissionsHelper.IsAllowed(user.Role, incoming, existing))
                return Rejected(table, rawId, SyncPushReasonHelper.NotAllowed, existing);
            if (!PushRowValidatorHelper.IsValid(incoming))
                return Rejected(table, rawId, SyncPushReasonHelper.Invalid, existing);
            if (await ReferenceProblemAsync(incoming, ct) is { } problem)
                return Rejected(table, rawId, problem, existing);

            // First change wins: an edit of a server row must be based on its current version. A stale one (or one
            // without baseSeq: the phone never saw the row) is rejected with the server row, for the app to ask.
            if (existing is not null && baseSeq != PushRowCodecHelper.ServerSeqOf(existing))
                return Rejected(table, rawId, SyncPushReasonHelper.Conflict, existing);

            var before = PushRowCodecHelper.Write(incoming);
            var rule = await ApplyRulesAsync(incoming, existing, state, ct);
            if (rule.Skip)
                return Changed(table, rawId, rule.Conflict!, null, existing);
            var target = rule.Target ?? existing;

            if (target is null || !PushRowCodecHelper.SameContent(incoming, target))
            {
                if (await SaveAsync(incoming, target, ct) is false)
                {
                    db.ChangeTracker.Clear();
                    return Rejected(table, rawId, SyncPushReasonHelper.Conflict, await db.FindAsync(type, [id], ct));
                }
            }
            if (incoming is Article article) state.TouchedArticles.Add(article.Id);
            if (incoming is StockMovement movement) state.TouchedArticles.Add(movement.ArticleId);

            // The saved row: the server one on an update (the trigger's new server_seq is read back), else the new one.
            var saved = target ?? incoming;
            if (rule.Conflict is null) return Accepted(table, rawId, saved);
            var after = PushRowCodecHelper.Write(incoming);
            return Changed(table, rawId, rule.Conflict, rule.FullRow ? after : PushRowCodecHelper.Diff(before, after), saved);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Inserts <paramref name="row"/>, or copies it onto <paramref name="target"/>. False on a DB conflict.</summary>
    private async Task<bool> SaveAsync(object row, object? target, CancellationToken ct)
    {
        if (target is null)
        {
            db.Add(row);
        }
        else
        {
            var entry = db.Entry(target);
            // server_seq is the trigger's: keep the loaded value so EF never writes it.
            row.GetType().GetProperty(nameof(Category.ServerSeq))!.SetValue(row, entry.Property(nameof(Category.ServerSeq)).CurrentValue);
            entry.CurrentValues.SetValues(row);
        }
        var tx = db.Database.CurrentTransaction!;
        await tx.CreateSavepointAsync("push_row", ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.ReleaseSavepointAsync("push_row", ct);
            return true;
        }
        catch (DbUpdateException e)
        {
            // A backstop: the checks above should catch every unique or foreign key violation.
            LogRowConflict(e.GetType().Name);
            await tx.RollbackToSavepointAsync("push_row", ct);
            return false;
        }
    }

    private sealed record RuleResult(string? Conflict = null, object? Target = null, bool FullRow = false, bool Skip = false);

    /// <summary>The conflict rules (docs/03-server-spec.md "Conflict rules"), a port of the app's BackupMerge.</summary>
    private async Task<RuleResult> ApplyRulesAsync(object incoming, object? existing, BatchState state, CancellationToken ct)
    {
        switch (incoming)
        {
            case Category c:
            {
                if (!await TakenAsync<Category>(c.Id, Lower<Category>(x => x.Name, c.Name), ct)) return new();
                c.Name = await PushSuffixHelper.FirstFreeAsync(c.Name, PushRowValidatorHelper.CategoryNameMax,
                    v => TakenAsync<Category>(c.Id, Lower<Category>(x => x.Name, v), ct));
                return new(SyncPushConflictHelper.CategoryRenamed);
            }
            case AttributeDefinition d when existing is AttributeDefinition old:
            {
                string? conflict = null;
                if (d.Key != old.Key)
                {
                    d.Key = old.Key;
                    d.CreatedAt = old.CreatedAt;
                    conflict = SyncPushConflictHelper.AttributeKeyKept;
                }
                KeepMeasure(d, old);
                return new(conflict);
            }
            case AttributeDefinition d:
            {
                var key = d.Key;
                var same = await db.Set<AttributeDefinition>().FirstOrDefaultAsync(x => x.Key == key, ct);
                if (same is null) return new();
                // Both are the same attribute: the incoming one updates the server one, links follow it.
                state.AttributeAliases[d.Id] = same.Id;
                d.Id = same.Id;
                d.CreatedAt = same.CreatedAt;
                KeepMeasure(d, same);
                return new(SyncPushConflictHelper.AttributeMerged, Target: same, FullRow: true);
            }
            case CategoryAttribute ca:
            {
                var (category, attribute, id) = (ca.CategoryId, ca.AttributeId, ca.Id);
                var linked = await db.Set<CategoryAttribute>()
                    .AnyAsync(x => x.CategoryId == category && x.AttributeId == attribute && x.Id != id, ct);
                return linked ? new(SyncPushConflictHelper.CategoryAttributeSkipped, Skip: true) : new();
            }
            case Location l:
            {
                string? conflict = null;
                if (l.ParentId is { } parent && await CreatesCycleAsync(l.Id, parent, ct))
                {
                    l.ParentId = (existing as Location)?.ParentId;
                    conflict = SyncPushConflictHelper.LocationParentKept;
                }
                if (l.Barcode is { } tag && await TagTakenAsync(tag, isArticle: false, l.Id, ct))
                {
                    l.Barcode = null;
                    conflict ??= SyncPushConflictHelper.BarcodeCleared;
                }
                return new(conflict);
            }
            case Article a:
            {
                string? conflict = null;
                if (a.Sku is { } sku && await TakenAsync<Article>(a.Id, Lower<Article>(x => x.Sku!, sku), ct))
                {
                    a.Sku = await PushSuffixHelper.FirstFreeAsync(sku, PushRowValidatorHelper.SkuMax,
                        v => TakenAsync<Article>(a.Id, Lower<Article>(x => x.Sku!, v), ct));
                    conflict = SyncPushConflictHelper.SkuRenamed;
                }
                if (a.Barcode is { } tag && await TagTakenAsync(tag, isArticle: true, a.Id, ct))
                {
                    a.Barcode = null;
                    conflict ??= SyncPushConflictHelper.BarcodeCleared;
                }
                return new(conflict);
            }
            case Item i:
            {
                if (i.SerialNumber is not { } serial
                    || !await TakenAsync<Item>(i.Id, Lower<Item>(x => x.SerialNumber!, serial), ct)) return new();
                i.SerialNumber = await PushSuffixHelper.FirstFreeAsync(serial, PushRowValidatorHelper.SerialMax,
                    v => TakenAsync<Item>(i.Id, Lower<Item>(x => x.SerialNumber!, v), ct));
                return new(SyncPushConflictHelper.SerialRenamed);
            }
            case StockMovement m:
                // The author is the token's, whatever the phone sent.
                m.CreatedBy = user.UserId;
                m.DeviceId = user.SessionId;
                return new();
            default:
                return new();
        }
    }

    /// <summary>
    /// A number attribute merged with a measure stays a measure (BackupMerge.keepMeasure): measure values don't
    /// read as numbers, while plain numbers read in the default unit.
    /// </summary>
    private static void KeepMeasure(AttributeDefinition winner, AttributeDefinition other)
    {
        if (winner.DataType != "number" || other.DataType != "measure") return;
        winner.DataType = "measure";
        winner.Unit = other.Unit;
        winner.UpdatedAt = Math.Max(winner.UpdatedAt, other.UpdatedAt);
    }

    private static bool SameMovement(StockMovement a, StockMovement b) =>
        a.ArticleId == b.ArticleId && a.ItemId == b.ItemId && a.LocationId == b.LocationId && a.Quantity == b.Quantity
        && a.Reason == b.Reason && a.Comment == b.Comment && a.OccurredAt == b.OccurredAt && a.CreatedAt == b.CreatedAt;

    /// <summary>
    /// Referenced rows must exist, soft-deleted or not: a movement for a deleted article is accepted, because stock
    /// truth wins. A unit of another article is invalid. A second open review for a location is review-open.
    /// </summary>
    private async Task<string?> ReferenceProblemAsync(object row, CancellationToken ct)
    {
        const string missing = SyncPushReasonHelper.MissingReference;
        switch (row)
        {
            case CategoryAttribute ca:
                return await ExistsAsync<Category>(ca.CategoryId, ct) && await ExistsAsync<AttributeDefinition>(ca.AttributeId, ct) ? null : missing;
            case Location l:
                return await ExistsAsync<Location>(l.ParentId, ct) ? null : missing;
            case Article a:
                return await ExistsAsync<Category>(a.CategoryId, ct) ? null : missing;
            case Item i:
                return await ExistsAsync<Article>(i.ArticleId, ct) && await ExistsAsync<Location>(i.LocationId, ct) ? null : missing;
            case StockMovement m:
                if (!await ExistsAsync<Article>(m.ArticleId, ct) || !await ExistsAsync<Location>(m.LocationId, ct)) return missing;
                return await ItemProblemAsync(m.ItemId, m.ArticleId, ct);
            case StockReview r:
            {
                if (!await ExistsAsync<Location>(r.LocationId, ct)) return missing;
                if (r.Status != "open" || r.DeletedAt is not null) return null;
                var (id, location) = (r.Id, r.LocationId);
                var open = await db.Set<StockReview>()
                    .AnyAsync(x => x.Id != id && x.LocationId == location && x.Status == "open" && x.DeletedAt == null, ct);
                return open ? SyncPushReasonHelper.ReviewOpen : null;
            }
            case StockReviewLine x:
                if (!await ExistsAsync<StockReview>(x.ReviewId, ct) || !await ExistsAsync<Article>(x.ArticleId, ct)
                    || !await ExistsAsync<Location>(x.LocationId, ct) || !await ExistsAsync<Location>(x.FoundLocationId, ct))
                    return missing;
                return await ItemProblemAsync(x.ItemId, x.ArticleId, ct);
            default:
                return null;
        }
    }

    private async Task<string?> ItemProblemAsync(Guid? itemId, Guid articleId, CancellationToken ct)
    {
        if (itemId is not { } id) return null;
        var owner = await db.Set<Item>().Where(i => i.Id == id).Select(i => (Guid?)i.ArticleId).FirstOrDefaultAsync(ct);
        return owner is null ? SyncPushReasonHelper.MissingReference : owner == articleId ? null : SyncPushReasonHelper.Invalid;
    }

    private async Task<bool> ExistsAsync<T>(Guid? id, CancellationToken ct) where T : class =>
        id is not { } value || await db.Set<T>().AnyAsync(e => EF.Property<Guid>(e, "Id") == value, ct);

    /// <summary>Another row (soft-deleted ones too, like the unique index) matches.</summary>
    private Task<bool> TakenAsync<T>(Guid self, Expression<Func<T, bool>> match, CancellationToken ct) where T : class =>
        db.Set<T>().Where(match).AnyAsync(e => EF.Property<Guid>(e, "Id") != self, ct);

    /// <summary><c>lower(column) = lower(value)</c>: names, SKUs and serial numbers are unique ignoring case, like the app.</summary>
    private static Expression<Func<T, bool>> Lower<T>(Expression<Func<T, string>> column, string value)
    {
        var lower = value.ToLowerInvariant();
        var body = Expression.Equal(
            Expression.Call(column.Body, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!),
            Expression.Constant(lower));
        return Expression.Lambda<Func<T, bool>>(body, column.Parameters);
    }

    /// <summary>One tag = one thing: a tag is unique across articles and locations.</summary>
    private async Task<bool> TagTakenAsync(string tag, bool isArticle, Guid self, CancellationToken ct) =>
        await db.Set<Article>().AnyAsync(a => a.Barcode == tag && (!isArticle || a.Id != self), ct)
        || await db.Set<Location>().AnyAsync(l => l.Barcode == tag && (isArticle || l.Id != self), ct);

    /// <summary>The new parent is the location itself or one of its descendants.</summary>
    private async Task<bool> CreatesCycleAsync(Guid self, Guid parent, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        for (Guid? current = parent; current is { } c; )
        {
            if (c == self) return true;
            if (!seen.Add(c)) return false; // a cycle already on the server, not through this location
            current = await db.Set<Location>().Where(l => l.Id == c).Select(l => l.ParentId).FirstOrDefaultAsync(ct);
        }
        return false;
    }

    // Each result carries the server_seq of the server row it leaves (none when there is no server row): the
    // phone's next baseSeq for that row.

    /// <summary>An accepted movement comes back with its author fields: the phone learns the author the server set.</summary>
    private static SyncPushResult Accepted(string table, string id, object serverRow) =>
        new(table, id, SyncPushStatusHelper.Accepted, ServerSeq: PushRowCodecHelper.ServerSeqOf(serverRow),
            Row: serverRow is StockMovement m
                ? new JsonObject { ["id"] = m.Id, ["createdBy"] = m.CreatedBy, ["deviceId"] = m.DeviceId }
                : null);

    private static SyncPushResult Changed(string table, string id, string conflict, JsonObject? row, object? serverRow) =>
        new(table, id, SyncPushStatusHelper.Changed, Conflict: conflict, Row: row,
            ServerSeq: serverRow is null ? null : PushRowCodecHelper.ServerSeqOf(serverRow));

    /// <summary>A rejected row comes back with the server's version of it, empty when there is none.</summary>
    private static SyncPushResult Rejected(string table, string id, string reason, object? serverRow) =>
        new(table, id, SyncPushStatusHelper.Rejected, Reason: reason, Row: serverRow is null ? [] : PushRowCodecHelper.Write(serverRow),
            ServerSeq: serverRow is null ? null : PushRowCodecHelper.ServerSeqOf(serverRow));

    [LoggerMessage(LogLevel.Information,
        "Push batch {BatchId} by user {UserId}: {Accepted} accepted, {Changed} changed, {Rejected} rejected, {Alerts} low-stock alerts")]
    private partial void LogApplied(Guid batchId, Guid userId, int accepted, int changed, int rejected, int alerts);

    [LoggerMessage(LogLevel.Information, "Push batch {BatchId} by user {UserId} already applied: saved answer returned")]
    private partial void LogReplayed(Guid batchId, Guid userId);

    [LoggerMessage(LogLevel.Warning, "Push batch {BatchId} by user {UserId} reuses the ID of another user's batch")]
    private partial void LogBatchIdReused(Guid batchId, Guid userId);

    [LoggerMessage(LogLevel.Warning, "Push row rejected by the database ({Error}); the pre-checks missed it")]
    private partial void LogRowConflict(string error);
}

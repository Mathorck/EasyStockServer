using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB;

/// <summary>
/// server_seq: one global sequence for every pullable table, set by a BEFORE INSERT OR UPDATE trigger that also
/// takes the sync advisory lock (docs/04-server-implementation-plan.md, 3.1). Writers are serialised, so a pull
/// snapshot always sees a prefix of the sequence and a cursor never skips a row. The sequence, the set_server_seq()
/// trigger function and the triggers are created by the migrations, as frozen SQL.
/// </summary>
public static class ServerSeq
{
    /// <summary>Key of the transaction-level advisory lock shared by the trigger, push and import.</summary>
    public const long SyncLockKey = 0x4553_5359_4E43; // "ESSYNC"

    /// <summary>
    /// Takes the sync lock for the current transaction. Push and import call it first, so their
    /// conflict checks read under the lock too. Taking it again in the same transaction is free.
    /// </summary>
    public static Task AcquireSyncLockAsync(this EasyStockDbContext db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The sync lock is transaction-scoped: begin a transaction first.");
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SyncLockKey})", ct);
    }
}

using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB;

/// <summary>
/// server_seq: one global sequence for every pullable table, set by a BEFORE INSERT OR UPDATE trigger that also
/// takes the sync advisory lock (docs/04-server-implementation-plan.md, 3.1). Writers are serialised, so a pull
/// snapshot always sees a prefix of the sequence and a cursor never skips a row.
/// </summary>
public static class ServerSeq
{
    public const string Sequence = "server_seq";
    public const string TriggerFunction = "set_server_seq";

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

    internal static string CreateFunctionSql =>
        $"""
        CREATE SEQUENCE {Sequence} AS bigint;
        CREATE FUNCTION {TriggerFunction}() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock({SyncLockKey});
            NEW.server_seq := nextval('{Sequence}');
            RETURN NEW;
        END
        $$;
        """;

    internal static string CreateTriggerSql(string table) =>
        $"CREATE TRIGGER {table}_server_seq BEFORE INSERT OR UPDATE ON {table} FOR EACH ROW EXECUTE FUNCTION {TriggerFunction}();";

    /// <summary>
    /// users: a new user always gets a server_seq; an update only when the synced columns (display_name,
    /// deleted_at) change, so logins and token refreshes do not wake every client's pull. Two triggers because a
    /// WHEN clause cannot mention OLD on insert.
    /// </summary>
    internal static string CreateUsersTriggersSql =>
        $"""
        CREATE TRIGGER users_server_seq_insert BEFORE INSERT ON users FOR EACH ROW EXECUTE FUNCTION {TriggerFunction}();
        CREATE TRIGGER users_server_seq_update BEFORE UPDATE ON users FOR EACH ROW
            WHEN (OLD.display_name IS DISTINCT FROM NEW.display_name OR OLD.deleted_at IS DISTINCT FROM NEW.deleted_at)
            EXECUTE FUNCTION {TriggerFunction}();
        """;

    internal static string DropUsersTriggersSql =>
        "DROP TRIGGER users_server_seq_update ON users; DROP TRIGGER users_server_seq_insert ON users;";

    internal static string DropFunctionSql =>
        $"DROP FUNCTION {TriggerFunction}() CASCADE; DROP SEQUENCE {Sequence};";
}

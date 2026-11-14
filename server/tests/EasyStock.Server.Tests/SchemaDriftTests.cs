using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Server.DB;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>
/// The migrated database must match the shared tables of the app (../EasyStock, or EASYSTOCK_APP_DIR), as of the app's
/// data schema version: the columns, nullability and indexes come from the app's Room schema JSON of that version
/// (shared/schemas/.../N.json), the column types from docs/schema.dbml (Room only knows SQLite affinities).
/// Server rules: timestamp and decimal are bigint, json is jsonb, server_seq on every table,
/// article.image_path is image_sha256 character(64).
/// </summary>
/// <remarks>
/// The app has two version constants in AppDatabase.kt. <c>DB_VERSION</c> is the local Room version: it also moves
/// for app-only changes (local indexes, app-only tables), which the server never sees. <c>DATA_SCHEMA_VERSION</c> is
/// the version of the shared data, sent in X-EasyStock-Schema-Version and written in backups: the latest DB_VERSION
/// whose synced tables are unchanged. The server follows DATA_SCHEMA_VERSION only.
/// </remarks>
public sealed partial class SchemaDriftTests(PostgresFixture pg)
{
    /// <summary>The tables only the server has (not in the app's schema.dbml).</summary>
    private static readonly string[] ServerOnlyTables =
    [
        "__EFMigrationsHistory",
        // Auth
        "users", "device_session", "invite", "public_invite", "promotion", "password_reset", "setup_state",
        // Images
        "image",
        // Settings
        "setting",
        // Push registration
        "push_registration",
        // Sync push and low-stock alerts
        "sync_batch", "low_stock_state", "low_stock_alert", "attribute_alias",
    ];

    /// <summary>
    /// Unique indexes the server adds on purpose. stock_review(location_id) is partial (open, not deleted): the dbml's
    /// "at most one open review per location (checked by the app)", which push enforces too (review-open).
    /// </summary>
    private static readonly HashSet<(string Table, string Columns)> ServerOnlyUniqueIndexes = [("stock_review", "location_id")];

    private const string AppDatabaseFile = "shared/src/commonMain/kotlin/ch/mathorck/easystock/data/db/AppDatabase.kt";
    private const string AppSchemasDir = "shared/schemas/ch.mathorck.easystock.data.db.AppDatabase";

    [Fact]
    public void Supported_schema_versions_contain_the_apps_data_schema_version()
    {
        var version = ReadDataSchemaVersion();
        Assert.True(EasyStock.Server.Helpers.CompatHelper.SupportedSchemaVersions.Contains(version),
            $"The app's DATA_SCHEMA_VERSION {version} is not in CompatHelper.SupportedSchemaVersions " +
            $"[{string.Join(", ", EasyStock.Server.Helpers.CompatHelper.SupportedSchemaVersions)}]");
    }

    /// <summary>The app's <c>DATA_SCHEMA_VERSION</c> (not DB_VERSION, see the remarks of this class).</summary>
    private static int ReadDataSchemaVersion()
    {
        var path = FindAppFile(AppDatabaseFile);
        var m = Regex.Match(File.ReadAllText(path), @"const\s+val\s+DATA_SCHEMA_VERSION\s*=\s*(\d+)");
        if (!m.Success)
            Assert.Fail($"const val DATA_SCHEMA_VERSION not found in {path}. It is the app's shared-data version (sent in " +
                        "X-EasyStock-Schema-Version and written in backups), the version this test compares the server with. " +
                        "If the app renamed or moved it, update SchemaDriftTests.");
        return int.Parse(m.Groups[1].Value);
    }

    /// <summary>The app repository's file, or null when the sibling repository isn't there.</summary>
    public static string? TryFindAppFile(string relative)
    {
        var env = Environment.GetEnvironmentVariable("EASYSTOCK_APP_DIR");
        if (!string.IsNullOrEmpty(env)) return File.Exists(Path.Combine(env, relative)) ? Path.Combine(env, relative) : null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "EasyStock", relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The app repository's file; skips the test when the app repository is absent.</summary>
    public static string FindAppFile(string relative)
    {
        var path = TryFindAppFile(relative);
        if (path == null)
            Assert.Skip($"App repository not found: put it next to this repository as ../EasyStock or set EASYSTOCK_APP_DIR ({relative}).");
        return path!;
    }

    [Fact]
    public async Task Migrated_database_matches_app_schema()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        factory.CreateClient();

        HashSet<string> modelTables;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            Assert.False(db.Database.HasPendingModelChanges(), "Model changed without a migration: run dotnet ef migrations add");
            modelTables = [.. db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>()];
        }

        var version = ReadDataSchemaVersion();
        var schemaPath = TryFindAppFile($"{AppSchemasDir}/{version}.json");
        if (schemaPath == null) Assert.Fail($"The app's Room schema for DATA_SCHEMA_VERSION {version} is missing: {AppSchemasDir}/{version}.json");
        var room = ReadRoomSchema(await File.ReadAllTextAsync(schemaPath!, ct));
        var dbml = await File.ReadAllTextAsync(FindAppFile("docs/schema.dbml"), ct);
        var types = ParseDbml(dbml);

        var problems = new List<string>();
        // The shared tables are the dbml's tables without the "App only" note. Their columns and nullability come from
        // the Room schema of the data version, their types from the dbml: the dbml describes the latest DB_VERSION,
        // whose synced tables are the same as the data version's by definition of DATA_SCHEMA_VERSION.
        var expected = new Dictionary<string, Dictionary<string, (string Type, bool Nullable)>>();
        foreach (var (table, dbmlCols) in types)
        {
            if (!room.Tables.TryGetValue(table, out var roomCols)) { problems.Add($"shared table {table} is not in {version}.json"); continue; }
            var cols = expected[table] = [];
            foreach (var (col, nullable) in roomCols)
            {
                if (dbmlCols.TryGetValue(col, out var t)) cols[col] = (t.Type, nullable);
                else problems.Add($"{table}.{col} is in {version}.json but not in schema.dbml (synced table changed without a new DATA_SCHEMA_VERSION?)");
            }
            foreach (var col in dbmlCols.Keys.Except(roomCols.Keys))
                problems.Add($"{table}.{col} is in schema.dbml but not in {version}.json (synced table changed without a new DATA_SCHEMA_VERSION?)");
        }
        var actual = await ReadDatabase(pg.ConnectionString, ct);

        foreach (var (table, cols) in expected)
        {
            if (!actual.TryGetValue(table, out var dbCols)) { problems.Add($"missing table {table}"); continue; }
            foreach (var (col, (type, nullable)) in cols)
            {
                var (name, wantType) = table == "article" && col == "image_path" ? ("image_sha256", "character(64)") : (col, type);
                if (!dbCols.TryGetValue(name, out var got)) problems.Add($"missing column {table}.{name}");
                else if (got != (wantType, nullable))
                    problems.Add($"{table}.{name}: expected {wantType} {(nullable ? "null" : "not null")}, got {got.Type} {(got.Nullable ? "null" : "not null")}");
            }
            foreach (var extra in dbCols.Keys.Except(cols.Keys).Except(["server_seq", "image_sha256"]))
                problems.Add($"extra column {table}.{extra} (not in {version}.json)");
            if (!dbCols.ContainsKey("server_seq")) problems.Add($"missing column {table}.server_seq");
        }
        // Server-only tables are listed in ServerOnlyTables; the model must not have any other table outside the
        // app's schema, and the database must not have any other table at all.
        foreach (var unlisted in modelTables.Except(expected.Keys).Except(ServerOnlyTables))
            problems.Add($"server-only table {unlisted} is not in ServerOnlyTables");
        foreach (var extra in actual.Keys.Except(expected.Keys).Except(ServerOnlyTables))
            problems.Add($"extra table {extra} (neither a shared table of {version}.json nor in ServerOnlyTables)");

        // Indexes: every index of the data version's shared tables must exist with the same columns and uniqueness.
        // Indexes the app added after it (in a DB_VERSION that kept the DATA_SCHEMA_VERSION) are local only and not
        // required. Extra plain indexes are fine (foreign keys, server_seq, lower() lookups, kept as their definition),
        // an extra unique index is not: it would reject rows the app accepts.
        var dbIndexes = await ReadIndexes(pg.ConnectionString, ct);
        foreach (var table in expected.Keys)
        {
            var indexes = room.Indexes.GetValueOrDefault(table) ?? [];
            var have = dbIndexes.GetValueOrDefault(table) ?? [];
            foreach (var index in indexes.Where(i => !have.Contains(i)))
                problems.Add($"missing {(index.Unique ? "unique " : "")}index {table}({index.Columns})");
            foreach (var extra in have.Where(i => i.Unique && !indexes.Contains(i) && !ServerOnlyUniqueIndexes.Contains((table, i.Columns))))
                problems.Add($"extra unique index {table}({extra.Columns}) (not in {version}.json)");
        }

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    private readonly record struct Index(string Columns, bool Unique);

    private sealed record RoomSchema(
        Dictionary<string, Dictionary<string, bool>> Tables, Dictionary<string, HashSet<Index>> Indexes);

    /// <summary>The columns (name to nullable) and the indexes of each table of a Room schema JSON.</summary>
    private static RoomSchema ReadRoomSchema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tables = new Dictionary<string, Dictionary<string, bool>>();
        var indexes = new Dictionary<string, HashSet<Index>>();
        foreach (var entity in doc.RootElement.GetProperty("database").GetProperty("entities").EnumerateArray())
        {
            var table = entity.GetProperty("tableName").GetString()!;
            tables[table] = entity.GetProperty("fields").EnumerateArray().ToDictionary(
                f => f.GetProperty("columnName").GetString()!,
                f => !(f.TryGetProperty("notNull", out var nn) && nn.GetBoolean()));
            indexes[table] = entity.TryGetProperty("indices", out var ix)
                ? [.. ix.EnumerateArray().Select(i => new Index(
                    string.Join(',', i.GetProperty("columnNames").EnumerateArray().Select(c => c.GetString())),
                    i.GetProperty("unique").GetBoolean()))]
                : [];
        }
        return new RoomSchema(tables, indexes);
    }

    /// <summary>The flags of a dbml attribute list (pk, unique, not null...), without notes, defaults or refs.</summary>
    private static HashSet<string> Attributes(string attrs) =>
        NoteRegex().Replace(attrs, "").Split(',').Select(x => x.Trim()).Where(x => !x.Contains(':', StringComparison.Ordinal)).ToHashSet();

    private static async Task<Dictionary<string, HashSet<Index>>> ReadIndexes(string cs, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT t.relname, ix.indisunique,
                   CASE WHEN ix.indexprs IS NULL THEN
                       (SELECT string_agg(a.attname, ',' ORDER BY k.ord)
                        FROM unnest(ix.indkey::int2[]) WITH ORDINALITY k(attnum, ord)
                        JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum)
                   ELSE pg_get_indexdef(ix.indexrelid) END
            FROM pg_index ix JOIN pg_class t ON t.oid = ix.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'public' AND NOT ix.indisprimary
            """, conn);
        var result = new Dictionary<string, HashSet<Index>>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (!result.TryGetValue(r.GetString(0), out var set)) result[r.GetString(0)] = set = [];
            set.Add(new Index(r.GetString(2), r.GetBoolean(1)));
        }
        return result;
    }

    private static Dictionary<string, Dictionary<string, (string Type, bool Nullable)>> ParseDbml(string dbml)
    {
        var tables = new Dictionary<string, Dictionary<string, (string, bool)>>();
        foreach (Match t in TableRegex().Matches(dbml))
        {
            if (t.Groups["head"].Value.Contains("note: 'App only", StringComparison.Ordinal)) continue;
            var cols = new Dictionary<string, (string, bool)>();
            foreach (var line in t.Groups["body"].Value.Split('\n'))
            {
                var c = ColumnRegex().Match(line);
                if (!c.Success) continue;
                var attrs = Attributes(c.Groups["attrs"].Value);
                cols[c.Groups["name"].Value] = (PgType(c.Groups["type"].Value), !(attrs.Contains("pk") || attrs.Contains("not null")));
            }
            tables[t.Groups["name"].Value] = cols;
        }
        return tables;
    }

    private static string PgType(string dbml) => dbml switch
    {
        "uuid" or "text" or "boolean" or "bigint" => dbml,
        "int" or "integer" => "integer",
        "timestamp" => "bigint",
        "json" => "jsonb",
        _ when dbml.StartsWith("decimal", StringComparison.Ordinal) => "bigint",
        _ when dbml.StartsWith("varchar(", StringComparison.Ordinal) => "character varying" + dbml[7..],
        _ when dbml.StartsWith("char(", StringComparison.Ordinal) => "character" + dbml[4..],
        _ => throw new InvalidOperationException($"Unknown dbml type {dbml}"),
    };

    private static async Task<Dictionary<string, Dictionary<string, (string Type, bool Nullable)>>> ReadDatabase(string cs, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull
            FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
            """, conn);
        var result = new Dictionary<string, Dictionary<string, (string, bool)>>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (!result.TryGetValue(r.GetString(0), out var cols)) result[r.GetString(0)] = cols = [];
            cols[r.GetString(1)] = (r.GetString(2), r.GetBoolean(3));
        }
        return result;
    }

    [GeneratedRegex(@"^Table (?<name>\w+)(?<head>[^{\n]*)\{(?<body>.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"^\s+(?<name>\w+) (?<type>[\w(),]+)(?: \[(?<attrs>.*)\])?\s*$")]
    private static partial Regex ColumnRegex();

    [GeneratedRegex(@"note:\s*'(?:[^'\\]|\\.)*'")]
    private static partial Regex NoteRegex();
}

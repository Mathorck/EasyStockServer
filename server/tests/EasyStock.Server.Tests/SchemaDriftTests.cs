using System.Text.RegularExpressions;
using EasyStock.Server.DB;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>
/// The migrated database must match the shared tables of the app's docs/schema.dbml (../EasyStock, or EASYSTOCK_APP_DIR).
/// Server rules: timestamp and decimal are bigint, json is jsonb, server_seq on every table,
/// article.image_path is image_sha256 character(64).
/// </summary>
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

    [Fact]
    public void Supported_schema_versions_contain_the_apps_db_version()
    {
        var kotlin = File.ReadAllText(FindAppFile("shared/src/commonMain/kotlin/ch/mathorck/easystock/data/db/AppDatabase.kt"));
        var m = Regex.Match(kotlin, @"const\s+val\s+DB_VERSION\s*=\s*(\d+)");
        Assert.True(m.Success, "DB_VERSION not found in AppDatabase.kt");
        Assert.Contains(int.Parse(m.Groups[1].Value), EasyStock.Server.Helpers.CompatHelper.SupportedSchemaVersions);
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

        var dbml = await File.ReadAllTextAsync(FindAppFile("docs/schema.dbml"), ct);
        var expected = ParseDbml(dbml);
        var actual = await ReadDatabase(pg.ConnectionString, ct);

        var problems = new List<string>();
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
                problems.Add($"extra column {table}.{extra} (not in schema.dbml)");
            if (!dbCols.ContainsKey("server_seq")) problems.Add($"missing column {table}.server_seq");
        }
        // Server-only tables are listed in ServerOnlyTables; the model must not have any other table outside the
        // app's schema, and the database must not have any other table at all.
        foreach (var unlisted in modelTables.Except(expected.Keys).Except(ServerOnlyTables))
            problems.Add($"server-only table {unlisted} is not in ServerOnlyTables");
        foreach (var extra in actual.Keys.Except(expected.Keys).Except(ServerOnlyTables))
            problems.Add($"extra table {extra} (neither in schema.dbml nor in ServerOnlyTables)");

        // Indexes: every dbml index must exist with the same columns and uniqueness. Extra plain indexes are
        // fine (foreign keys, server_seq, lower() lookups, kept as their definition), an extra unique index is not: it would reject rows the app accepts.
        var dbIndexes = await ReadIndexes(pg.ConnectionString, ct);
        foreach (var (table, indexes) in ParseIndexes(dbml))
        {
            var have = dbIndexes.GetValueOrDefault(table) ?? [];
            foreach (var index in indexes.Where(i => !have.Contains(i)))
                problems.Add($"missing {(index.Unique ? "unique " : "")}index {table}({index.Columns})");
            foreach (var extra in have.Where(i => i.Unique && !indexes.Contains(i) && !ServerOnlyUniqueIndexes.Contains((table, i.Columns))))
                problems.Add($"extra unique index {table}({extra.Columns}) (not in schema.dbml)");
        }

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    private readonly record struct Index(string Columns, bool Unique);

    private static Dictionary<string, HashSet<Index>> ParseIndexes(string dbml)
    {
        var result = new Dictionary<string, HashSet<Index>>();
        foreach (Match t in TableRegex().Matches(dbml))
        {
            if (t.Groups["head"].Value.Contains("note: 'App only", StringComparison.Ordinal)) continue;
            var indexes = result[t.Groups["name"].Value] = [];
            var inIndexes = false;
            foreach (var line in t.Groups["body"].Value.Split('\n').Select(l => l.Trim()))
            {
                if (line == "indexes {") { inIndexes = true; continue; }
                if (line == "}") { inIndexes = false; continue; }
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                var attrs = AttrRegex().Match(line) is { Success: true } a ? Attributes(a.Groups[1].Value) : [];
                if (inIndexes)
                {
                    var cols = line.Split('[')[0].Trim().Trim('(', ')').Replace(" ", "", StringComparison.Ordinal);
                    if (!attrs.Contains("pk")) indexes.Add(new Index(cols, attrs.Contains("unique")));
                }
                else if (attrs.Contains("unique") && ColumnRegex().Match("  " + line) is { Success: true } c)
                    indexes.Add(new Index(c.Groups["name"].Value, Unique: true));
            }
        }
        return result;
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

    [GeneratedRegex(@"\[(.*)\]\s*$")]
    private static partial Regex AttrRegex();

    [GeneratedRegex(@"note:\s*'(?:[^'\\]|\\.)*'")]
    private static partial Regex NoteRegex();
}

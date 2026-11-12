using System.Text.Json.Nodes;
using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Handlers.Sync;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static EasyStock.Server.Tests.Infrastructure.SyncPushKit;

namespace EasyStock.Server.Tests;

/// <summary>
/// The app's docs/test-cases/merge/*.json, run through POST /sync/push: <c>existing</c> is seeded on the server,
/// <c>incoming</c> pushed by a user of <c>actorRole</c>. Each case runs in its own database. Short IDs ("c1") are
/// mapped to UUIDs. Results not listed in <c>expected.results</c> must be accepted; listed rows are partial.
/// </summary>
public sealed class SyncPushMergeCaseTests(PostgresFixture pg)
{
    /// <summary>Cases whose BackupMerge rule the server deliberately doesn't follow.</summary>
    private static readonly Dictionary<string, string> NotForPush = new()
    {
        ["newer-change-wins"] = "BackupMerge keeps the newer updatedAt; push is last write wins in receive order "
            + "(docs/03-server-spec.md, Push processing). See SyncPushTests.Last_write_wins_in_receive_order_not_by_updated_at.",
    };

    public static TheoryData<string> Cases()
    {
        var dir = Path.Combine(Path.GetDirectoryName(SchemaDriftTests.FindAppFile("docs/schema.dbml"))!, "test-cases", "merge");
        return new TheoryData<string>(Directory.GetFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).Order()!);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Merge_case(string name)
    {
        if (NotForPush.TryGetValue(name, out var reason)) Assert.Skip(reason);
        var ct = TestContext.Current.CancellationToken;
        var test = JsonNode.Parse(await File.ReadAllTextAsync(SchemaDriftTests.FindAppFile($"docs/test-cases/merge/{name}.json"), ct))!;
        var ids = new IdMap();

        var database = $"merge_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(pg.ConnectionString))
        {
            await admin.OpenAsync(ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync(ct);
        }
        var cs = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = database }.ConnectionString;
        using var factory = new ApiFactory(cs);
        factory.CreateClient(); // migrates the database

        // existing
        var existing = test["existing"]?.AsObject() ?? [];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            foreach (var table in PushTablesHelper.PushOrder.Where(existing.ContainsKey))
                foreach (var row in existing[table]!.AsArray())
                    db.Add(PushRowCodecHelper.TryRead(table, ids.ToServer(row!).AsObject())
                        ?? throw new InvalidOperationException($"{name}: existing {table} row doesn't parse"));
            await db.SaveChangesAsync(ct);
        }

        // incoming
        var role = Permissions.ParseRole((string?)test["actorRole"]) ?? throw new InvalidOperationException("actorRole");
        var incoming = test["incoming"]!.AsObject();
        var body = new JsonObject
        {
            ["batchId"] = Guid.NewGuid().ToString(),
            ["rows"] = ids.ToServer(incoming),
        };
        var answer = await factory.PushClient(await factory.SignInAsync(role)).PushAsync(body);
        Assert.Equal(System.Net.HttpStatusCode.OK, answer.Status);

        // expected.results: the set of non-accepted results
        var actual = answer.Json["results"]!.AsArray().Select(r => ids.ToApp(r!).AsObject())
            .Where(r => (string?)r["status"] != "accepted").ToDictionary(r => ((string?)r["table"], (string?)r["id"]));
        var expected = test["expected"]!["results"]!.AsArray().Select(r => r!.AsObject()).ToList();
        Assert.Equal(expected.Select(r => ((string?)r["table"], (string?)r["id"])).Order(), actual.Keys.Order());
        foreach (var want in expected)
        {
            var got = actual[((string?)want["table"], (string?)want["id"])];
            Assert.Equal((string?)want["status"], (string?)got["status"]);
            Assert.Equal((string?)want["conflict"], (string?)got["conflict"]);
            if (want["row"] is JsonObject wantRow) AssertPartial(wantRow, got["row"]?.AsObject() ?? [], $"{want["table"]} {want["id"]} row");
        }

        // expected.final: the server rows afterwards, partial
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            foreach (var (table, rows) in test["expected"]!["final"]?.AsObject() ?? [])
                foreach (var want in rows!.AsArray().Select(r => r!.AsObject()))
                {
                    var id = ids.Server((string)want["id"]!);
                    var entity = await db.FindAsync(PushTablesHelper.EntityType(table)!, [id], ct);
                    Assert.True(entity is not null, $"final {table} {want["id"]} missing");
                    AssertPartial(want, ids.ToApp(PushRowCodecHelper.Write(entity)).AsObject(), $"final {table} {want["id"]}");
                }
        }
    }

    private static void AssertPartial(JsonObject want, JsonObject got, string what)
    {
        foreach (var (field, value) in want)
        {
            Assert.True(got.ContainsKey(field), $"{what}: no {field}");
            Assert.Equal(value?.ToJsonString() ?? "null", got[field]?.ToJsonString() ?? "null");
        }
    }

    /// <summary>Short test IDs to UUIDs and back: <c>id</c> and every <c>…Id</c> string field.</summary>
    private sealed class IdMap
    {
        private readonly Dictionary<string, Guid> _toServer = [];
        private readonly Dictionary<string, string> _toApp = [];

        public Guid Server(string shortId)
        {
            if (!_toServer.TryGetValue(shortId, out var id))
            {
                _toServer[shortId] = id = Guid.NewGuid();
                _toApp[id.ToString()] = shortId;
            }
            return id;
        }

        public JsonNode ToServer(JsonNode node) => Rewrite(node, s => Server(s).ToString());

        public JsonNode ToApp(JsonNode node) => Rewrite(node, s => _toApp.GetValueOrDefault(s, s));

        private static JsonNode Rewrite(JsonNode node, Func<string, string> map)
        {
            var copy = node.DeepClone();
            Walk(copy, map);
            return copy;
        }

        private static void Walk(JsonNode? node, Func<string, string> map)
        {
            switch (node)
            {
                case JsonArray array:
                    foreach (var item in array) Walk(item, map);
                    break;
                case JsonObject obj:
                    foreach (var (field, value) in obj.ToList())
                    {
                        if ((field == "id" || field.EndsWith("Id", StringComparison.Ordinal)) && value is JsonValue v && v.TryGetValue<string>(out var s))
                            obj[field] = map(s);
                        else
                            Walk(value, map);
                    }
                    break;
            }
        }
    }
}

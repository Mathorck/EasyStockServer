using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Interfaces;

namespace EasyStock.Server.Tests;

/// <summary>
/// GET/POST /api/v1/admin/backup. Merge tests share the container database (unique ids and names, nothing assumes
/// an empty database); replace and export tests each get a database of their own in the same container.
/// </summary>
public sealed class AdminBackupTests(PostgresFixture pg) : IAsyncLifetime
{
    private const string Url = "/api/v1/admin/backup";
    private readonly List<ApiFactory> _factories = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var f in _factories) f.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private sealed record Server(ApiFactory Factory, HttpClient Client);

    private Task<Server> SharedAsync(Role role = Role.Admin) => StartAsync(pg.ConnectionString, role);

    private async Task<Server> FreshAsync(Role role = Role.Admin)
    {
        var name = "bk_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(pg.ConnectionString))
        {
            await admin.OpenAsync(TestContext.Current.CancellationToken);
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        return await StartAsync(new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name, Pooling = false }.ConnectionString, role);
    }

    private async Task<Server> StartAsync(string connectionString, Role role)
    {
        var factory = new ApiFactory(connectionString);
        _factories.Add(factory);
        return new Server(factory, factory.ClientFor(await factory.SignInAsync(role)));
    }

    private static string NewId() => Guid.NewGuid().ToString("D");

    private static Dictionary<string, object?> Cat(string id, string name, long updated = 1000) => new()
    {
        ["id"] = id, ["name"] = name, ["description"] = null, ["name_template"] = null,
        ["created_at"] = 1L, ["updated_at"] = updated, ["deleted_at"] = null,
    };

    private static Dictionary<string, object?> Loc(string id, string name, string? parent = null, long updated = 1000) => new()
    {
        ["id"] = id, ["name"] = name, ["parent_id"] = parent, ["icon"] = null, ["color"] = null, ["barcode"] = null,
        ["created_at"] = 1L, ["updated_at"] = updated, ["deleted_at"] = null,
    };

    private static Dictionary<string, object?> Art(string id, string name, string? sku = null, string? category = null,
        string? image = null, string tracking = "quantity", long updated = 1000) => new()
    {
        ["id"] = id, ["sku"] = sku, ["name"] = name, ["name_auto"] = false, ["description"] = null, ["category_id"] = category,
        ["tracking"] = tracking, ["unit"] = "pce", ["barcode"] = null, ["threshold"] = null, ["image_path"] = image,
        ["attributes"] = null, ["archived"] = false, ["created_at"] = 1L, ["updated_at"] = updated, ["deleted_at"] = null,
    };

    private static Dictionary<string, object?> Itm(string id, string article, string? serial, string? location = null) => new()
    {
        ["id"] = id, ["article_id"] = article, ["serial_number"] = serial, ["status"] = "available", ["location_id"] = location,
        ["attributes"] = null, ["archived"] = false, ["created_at"] = 1L, ["updated_at"] = 1000L, ["deleted_at"] = null,
    };

    private static Dictionary<string, object?> Mov(string id, string article, string location, long qty, string? item = null,
        string? createdBy = null) => new()
    {
        ["id"] = id, ["article_id"] = article, ["item_id"] = item, ["location_id"] = location, ["quantity"] = qty,
        ["reason"] = "purchase", ["comment"] = null, ["occurred_at"] = 1000L, ["created_at"] = 1000L,
        ["created_by"] = createdBy, ["device_id"] = null,
    };

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, .. RandomNumberGenerator.GetBytes(64)];

    private static byte[] Zip(IEnumerable<(string Name, byte[] Content)> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(content);
            }
        return ms.ToArray();
    }

    private static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value);

    private static byte[] Backup(
        Dictionary<string, List<Dictionary<string, object?>>>? tables = null,
        Dictionary<string, byte[]>? images = null,
        string format = "easystock-backup", int formatVersion = 1, int schemaVersion = 10,
        params (string Name, byte[] Content)[] extra)
    {
        var entries = new List<(string, byte[])>
        {
            ("manifest.json", Json(new Dictionary<string, object?>
            {
                ["format"] = format, ["format_version"] = formatVersion, ["app_version"] = "1.0.0", ["schema_version"] = schemaVersion,
                ["created_at"] = "2026-01-01T00:00:00Z", ["counts"] = new Dictionary<string, int>(),
            })),
        };
        foreach (var (table, rows) in tables ?? []) entries.Add(($"tables/{table}.json", Json(rows)));
        foreach (var (path, bytes) in images ?? []) entries.Add((path, bytes));
        entries.AddRange(extra);
        return Zip(entries);
    }

    private static Dictionary<string, List<Dictionary<string, object?>>> Tables(
        List<Dictionary<string, object?>>? category = null, List<Dictionary<string, object?>>? location = null,
        List<Dictionary<string, object?>>? article = null, List<Dictionary<string, object?>>? item = null,
        List<Dictionary<string, object?>>? movement = null)
    {
        var t = new Dictionary<string, List<Dictionary<string, object?>>>();
        if (category is not null) t["category"] = category;
        if (location is not null) t["location"] = location;
        if (article is not null) t["article"] = article;
        if (item is not null) t["item"] = item;
        if (movement is not null) t["stock_movement"] = movement;
        return t;
    }

    private static Task<HttpResponseMessage> Post(Server s, byte[] body, string query = "?mode=merge")
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return s.Client.PostAsync(Url + query, content, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return doc.RootElement.Clone();
    }

    private static async Task<string> ProblemType(HttpResponseMessage r) => (await ReadJson(r)).GetProperty("type").GetString()!;

    private static async Task<T> WithDb<T>(Server s, Func<EasyStockDbContext, Task<T>> action)
    {
        await using var scope = s.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<EasyStockDbContext>());
    }

    private static async Task<byte[]> Export(Server s)
    {
        var r = await s.Client.GetAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    private static Dictionary<string, byte[]> Unzip(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var ms = new MemoryStream();
            using var s = e.Open();
            s.CopyTo(ms);
            return ms.ToArray();
        });
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private async Task<byte[]> SeededBackupAsync(string categoryName, byte[]? photo = null)
    {
        var cat = NewId();
        var root = NewId();
        var shelf = NewId();
        var art = NewId();
        var serialArt = NewId();
        var unit = NewId();
        var photoPath = photo is null ? null : $"images/{NewId()}.jpg";
        await Task.CompletedTask;
        return Backup(
            Tables(
                category: [Cat(cat, categoryName)],
                location: [Loc(root, "Warehouse " + root), Loc(shelf, "Shelf " + shelf, root)],
                article: [Art(art, "Bolt " + art, "SKU-" + art, cat, photoPath), Art(serialArt, "Drill " + serialArt, tracking: "serial")],
                item: [Itm(unit, serialArt, "SN-" + unit)],
                movement:
                [
                    Mov(NewId(), art, shelf, 5000),
                    Mov(NewId(), serialArt, root, 1000, unit),
                ]),
            photo is null ? null : new Dictionary<string, byte[]> { [photoPath!] = photo });
    }

    // ---- access --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Only_administrators_can_export_or_import()
    {
        var ct = TestContext.Current.CancellationToken;
        var viewer = await SharedAsync(Role.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync(Url, ct)).StatusCode);
        var denied = await Post(viewer, Backup());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var anonymous = new ApiFactory(pg.ConnectionString);
        _factories.Add(anonymous);
        var client = anonymous.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url, ct)).StatusCode);
    }

    [Fact]
    public async Task Mode_must_be_merge_or_replace()
    {
        var s = await SharedAsync();
        foreach (var query in new[] { "", "?mode=", "?mode=wipe", "?mode=MERGE" })
        {
            var r = await Post(s, Backup(), query);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("validation-failed", await ProblemType(r));
        }
    }

    // ---- export --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Export_is_an_app_backup_with_every_table_and_the_photos()
    {
        var s = await FreshAsync();
        var photo = Jpeg();
        var seeded = await Post(s, await SeededBackupAsync("Fasteners", photo), "?mode=replace");
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);

        var response = await s.Client.GetAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith(".easystock", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var files = Unzip(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        using var manifest = JsonDocument.Parse(files["manifest.json"]);
        Assert.Equal("easystock-backup", manifest.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, manifest.RootElement.GetProperty("format_version").GetInt32());
        Assert.Equal(10, manifest.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("counts").GetProperty("category").GetInt32());
        Assert.Equal(2, manifest.RootElement.GetProperty("counts").GetProperty("stock_movement").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("counts").GetProperty("images").GetInt32());

        foreach (var table in new[] { "category", "attribute_definition", "category_attribute", "location", "article", "item",
                     "stock_movement", "stock_review", "stock_review_line" })
            Assert.True(files.ContainsKey($"tables/{table}.json"), table);

        using var articles = JsonDocument.Parse(files["tables/article.json"]);
        var path = articles.RootElement.EnumerateArray().Select(a => a.GetProperty("image_path").GetString()).Single(p => p is not null)!;
        Assert.Matches(@"^images/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.jpg$", path);
        Assert.Equal(photo, files[path]);
    }

    [Fact]
    public async Task Export_of_an_empty_server_is_still_a_valid_backup()
    {
        var s = await FreshAsync();
        var files = Unzip(await Export(s));
        Assert.Equal("[]", Encoding.UTF8.GetString(files["tables/article.json"]));
        var imported = await Post(await FreshAsync(), await Export(s), "?mode=replace");
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
    }

    [Fact]
    public async Task Export_leaves_out_photos_the_server_no_longer_has()
    {
        var s = await FreshAsync();
        var photo = Jpeg();
        await Post(s, await SeededBackupAsync("Gone", photo), "?mode=replace");
        Directory.Delete(Path.Combine(s.Factory.DataDir, "images"), true);

        var files = Unzip(await Export(s));
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("images/", StringComparison.Ordinal));
        using var articles = JsonDocument.Parse(files["tables/article.json"]);
        Assert.All(articles.RootElement.EnumerateArray(), a => Assert.Equal(JsonValueKind.Null, a.GetProperty("image_path").ValueKind));
    }

    [Fact]
    public async Task Export_then_replace_import_reproduces_the_data_exactly()
    {
        var a = await FreshAsync();
        var b = await FreshAsync();
        await Post(a, await SeededBackupAsync("Round trip", Jpeg()), "?mode=replace");
        await Post(b, await SeededBackupAsync("Other data"), "?mode=replace");

        var exported = await Export(a);
        var r = await Post(b, exported, "?mode=replace");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("replace", (await ReadJson(r)).GetProperty("mode").GetString());

        var before = Unzip(exported);
        var after = Unzip(await Export(b));
        Assert.Equal(before.Keys.Where(k => k != "manifest.json").Order(), after.Keys.Where(k => k != "manifest.json").Order());
        foreach (var key in before.Keys.Where(k => k != "manifest.json"))
            Assert.True(before[key].SequenceEqual(after[key]), key);
    }

    // ---- replace -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Replace_removes_the_old_data_and_what_refers_to_it()
    {
        var s = await FreshAsync();
        var first = await SeededBackupAsync("Old");
        await Post(s, first, "?mode=replace");
        await WithDb(s, async db =>
        {
            var article = await db.Articles.FirstAsync(a => a.Sku != null, TestContext.Current.CancellationToken);
            db.Set<LowStockAlert>().Add(new LowStockAlert { Id = Guid.NewGuid(), ArticleId = article.Id, CrossedAt = 1, Stock = 0 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });

        var r = await Post(s, await SeededBackupAsync("New"), "?mode=replace");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(["New"], await WithDb(s, db => db.Categories.Select(c => c.Name).ToListAsync(ct)));
        Assert.Equal(0, await WithDb(s, db => db.Set<LowStockAlert>().CountAsync(ct)));
        Assert.Equal(2, await WithDb(s, db => db.Articles.CountAsync(ct)));
        Assert.Equal(2, await WithDb(s, db => db.StockMovements.CountAsync(ct)));
    }

    [Fact]
    public async Task A_refused_replace_changes_nothing()
    {
        var s = await FreshAsync();
        await Post(s, await SeededBackupAsync("Keep me"), "?mode=replace");
        var ct = TestContext.Current.CancellationToken;

        // An article pointing to a category the backup does not contain.
        var broken = Backup(Tables(category: [Cat(NewId(), "Replacement")], article: [Art(NewId(), "Orphan", category: NewId())]));
        var r = await Post(s, broken, "?mode=replace");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("backup-invalid", await ProblemType(r));
        Assert.Equal(["Keep me"], await WithDb(s, db => db.Categories.Select(c => c.Name).ToListAsync(ct)));
    }

    [Fact]
    public async Task Replace_recomputes_item_locations_and_drops_movement_authors()
    {
        var s = await FreshAsync();
        var art = NewId();
        var root = NewId();
        var unit = NewId();
        var author = NewId();
        var body = Backup(Tables(
            location: [Loc(root, "Root")], article: [Art(art, "Drill", tracking: "serial")], item: [Itm(unit, art, "SN-1", location: null)],
            movement: [Mov(NewId(), art, root, 1000, unit, createdBy: author)]));
        Assert.Equal(HttpStatusCode.OK, (await Post(s, body, "?mode=replace")).StatusCode);

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(Guid.Parse(root), await WithDb(s, db => db.Items.Select(i => i.LocationId).SingleAsync(ct)));
        Assert.Null(await WithDb(s, db => db.StockMovements.Select(m => m.CreatedBy).SingleAsync(ct)));
    }

    // ---- merge ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Merge_adds_new_rows_and_a_second_merge_changes_nothing()
    {
        var s = await SharedAsync();
        var body = await SeededBackupAsync("Merge " + NewId());
        var first = await ReadJson(await Post(s, body));
        Assert.Equal("merge", first.GetProperty("mode").GetString());
        Assert.Equal(1, first.GetProperty("added").GetProperty("category").GetInt32());
        Assert.Equal(2, first.GetProperty("added").GetProperty("location").GetInt32());
        Assert.Equal(2, first.GetProperty("added").GetProperty("stock_movement").GetInt32());

        var second = await ReadJson(await Post(s, body));
        Assert.Empty(second.GetProperty("added").EnumerateObject());
        Assert.Empty(second.GetProperty("updated").EnumerateObject());
        Assert.Equal(0, second.GetProperty("conflicts").GetArrayLength());
    }

    [Fact]
    public async Task Merge_keeps_the_newest_version_and_the_local_one_on_a_tie()
    {
        var s = await SharedAsync();
        var ct = TestContext.Current.CancellationToken;
        var id = NewId();
        var name = "v-" + NewId();
        async Task<JsonElement> Send(string n, long updated) =>
            await ReadJson(await Post(s, Backup(Tables(category: [Cat(id, n, updated)]))));
        async Task<string> Current() => await WithDb(s, db => db.Categories.Where(c => c.Id == Guid.Parse(id)).Select(c => c.Name).SingleAsync(ct));

        await Send(name + "-a", 1000);
        var newer = await Send(name + "-b", 2000);
        Assert.Equal(1, newer.GetProperty("updated").GetProperty("category").GetInt32());
        Assert.Equal(name + "-b", await Current());

        await Send(name + "-older", 1500);
        Assert.Equal(name + "-b", await Current());
        await Send(name + "-tie", 2000);
        Assert.Equal(name + "-b", await Current());
    }

    [Fact]
    public async Task Merge_renames_a_clashing_unique_value_and_reports_it()
    {
        var s = await SharedAsync();
        var ct = TestContext.Current.CancellationToken;
        var name = "Clash " + NewId();
        var local = NewId();
        var incoming = NewId();
        await Post(s, Backup(Tables(category: [Cat(local, name)])));

        var r = await ReadJson(await Post(s, Backup(Tables(category: [Cat(incoming, name.ToLowerInvariant())]))));
        var conflict = Assert.Single(r.GetProperty("conflicts").EnumerateArray());
        Assert.Equal("CATEGORY_RENAMED", conflict.GetProperty("type").GetString());
        Assert.Equal("category", conflict.GetProperty("table").GetString());
        Assert.Equal(incoming, conflict.GetProperty("rowId").GetString());

        var names = await WithDb(s, db => db.Categories.Where(c => c.Id == Guid.Parse(local) || c.Id == Guid.Parse(incoming))
            .Select(c => c.Name).ToListAsync(ct));
        Assert.Equal(2, names.Count);
        Assert.Equal(2, names.Select(n => n.ToLowerInvariant()).Distinct().Count());
        Assert.Contains(name, names);
    }

    [Fact]
    public async Task Merge_stores_photos_by_hash_and_points_the_article_at_them()
    {
        var s = await SharedAsync();
        var ct = TestContext.Current.CancellationToken;
        var photo = Jpeg();
        var art = NewId();
        var path = $"images/{NewId()}.jpg";
        var r = await ReadJson(await Post(s, Backup(Tables(article: [Art(art, "Photo " + art, image: path)]),
            new Dictionary<string, byte[]> { [path] = photo })));
        Assert.Equal(1, r.GetProperty("photos").GetInt32());

        Assert.Equal(Sha(photo), await WithDb(s, db => db.Articles.Where(a => a.Id == Guid.Parse(art)).Select(a => a.ImageSha256).SingleAsync(ct)));
        await using var scope = s.Factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IBackupImageHandler>().ExistsAsync(Sha(photo), ct));
    }

    [Fact]
    public async Task An_article_whose_photo_is_not_in_the_archive_is_imported_without_one()
    {
        var s = await SharedAsync();
        var art = NewId();
        var r = await Post(s, Backup(Tables(article: [Art(art, "No photo " + art, image: $"images/{NewId()}.jpg")])));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Null(await WithDb(s, db => db.Articles.Where(a => a.Id == Guid.Parse(art)).Select(a => a.ImageSha256).SingleAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Merge_fixes_the_cached_item_location_from_the_movements()
    {
        var s = await SharedAsync();
        var art = NewId();
        var shelf = NewId();
        var unit = NewId();
        await Post(s, Backup(Tables(
            location: [Loc(shelf, "Shelf " + shelf)], article: [Art(art, "Saw " + art, tracking: "serial")],
            item: [Itm(unit, art, "SN-" + unit, location: null)],
            movement: [Mov(NewId(), art, shelf, 1000, unit)])));
        Assert.Equal(Guid.Parse(shelf), await WithDb(s, db => db.Items.Where(i => i.Id == Guid.Parse(unit)).Select(i => i.LocationId).SingleAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_refused_merge_changes_nothing()
    {
        var s = await SharedAsync();
        var good = NewId();
        // Valid category plus a location that is its own parent: the whole file is refused.
        var loop = NewId();
        var r = await Post(s, Backup(Tables(category: [Cat(good, "Never " + good)], location: [Loc(loop, "Loop", loop)])));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("backup-invalid", await ProblemType(r));
        Assert.False(await WithDb(s, db => db.Categories.AnyAsync(c => c.Id == Guid.Parse(good), TestContext.Current.CancellationToken)));
    }

    // ---- bad files -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Files_that_are_not_readable_backups_are_refused_with_a_reason()
    {
        var s = await SharedAsync();
        async Task Expect(byte[] body, HttpStatusCode status, string type)
        {
            var r = await Post(s, body);
            Assert.Equal(status, r.StatusCode);
            Assert.Equal(type, await ProblemType(r));
        }

        await Expect(Encoding.UTF8.GetBytes("hello"), HttpStatusCode.UnprocessableEntity, "not-a-backup");
        await Expect([], HttpStatusCode.UnprocessableEntity, "not-a-backup");
        await Expect([.. "ESBK"u8, .. RandomNumberGenerator.GetBytes(100)], HttpStatusCode.UnprocessableEntity, "backup-encrypted");
        await Expect(Zip([("something.txt", [1, 2, 3])]), HttpStatusCode.UnprocessableEntity, "not-a-backup");
        await Expect(Backup(format: "other"), HttpStatusCode.UnprocessableEntity, "not-a-backup");
        await Expect(Backup(formatVersion: 2), HttpStatusCode.UnprocessableEntity, "backup-newer-version");
        await Expect(Backup(schemaVersion: 12), HttpStatusCode.UnprocessableEntity, "backup-newer-version");
        await Expect(Backup(extra: ("tables/category.json", "{ not json"u8.ToArray())), HttpStatusCode.UnprocessableEntity, "backup-invalid");
        await Expect(Backup(extra: ("tables/category.json", "{\"a\":1}"u8.ToArray())), HttpStatusCode.UnprocessableEntity, "backup-invalid");
    }

    [Fact]
    public async Task Backup_with_the_newest_supported_schema_version_is_accepted()
    {
        var r = await Post(await SharedAsync(), Backup(schemaVersion: 11));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Inconsistent_rows_are_refused()
    {
        var s = await SharedAsync();
        var dup = NewId();
        var n = 0;
        foreach (var body in new[]
                 {
                     Backup(Tables(category: [Cat(dup, "A " + dup), Cat(dup, "B " + dup)])),                       // duplicate id
                     Backup(Tables(category: [Cat("not-a-uuid", "X")])),                                          // bad id
                     Backup(Tables(category: [Cat(NewId(), "Same"), Cat(NewId(), "Same")])),                       // unique clash inside the file
                     Backup(Tables(movement: [Mov(NewId(), NewId(), NewId(), 1)])),                               // dangling references
                     Backup(Tables(category: [Cat(NewId(), new string('x', 101))])),                              // too long for the column
                     Backup(Tables(category: [Cat(NewId(), "Nul\0")])),                                           // NUL character
                 })
        {
            var r = await Post(s, body);
            Assert.True(r.StatusCode == HttpStatusCode.UnprocessableEntity, $"case {n}: {r.StatusCode}");
            Assert.Equal("backup-invalid", await ProblemType(r));
            n++;
        }
    }

    [Fact]
    public async Task A_photo_that_is_not_a_jpeg_is_refused_and_odd_entry_names_are_ignored()
    {
        var s = await SharedAsync();
        var art = NewId();
        var path = $"images/{NewId()}.jpg";
        var bad = await Post(s, Backup(Tables(article: [Art(art, "Fake " + art, image: path)]),
            new Dictionary<string, byte[]> { [path] = Encoding.UTF8.GetBytes("not a jpeg") }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.False(await WithDb(s, db => db.Articles.AnyAsync(a => a.Id == Guid.Parse(art), TestContext.Current.CancellationToken)));

        var odd = await Post(s, Backup(extra: [("../../evil.txt", [1]), ("images/../evil.jpg", Jpeg()), ("images/nope.jpg", Jpeg())]));
        Assert.Equal(HttpStatusCode.OK, odd.StatusCode);
    }
}

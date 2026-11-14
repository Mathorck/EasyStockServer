using System.Net;
using System.Reflection;
using System.Text.Json;
using EasyStock.Server.Filters;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Controllers;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Sync;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>
/// GET /api/v1/sync/pull. Each test (class instance) has a database of its own, so no other test moves the cursor;
/// tests still read the cursor just before inserting their own rows, and only assert on their own ids.
/// </summary>
public sealed class SyncPullTests(PostgresFixture pg) : IAsyncLifetime
{
    // A database of its own: signing in adds users rows, which take part in the pull window like shared rows, so
    // sign-ins of tests running in parallel elsewhere would move this class's page cuts.
    private ApiFactory _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = new ApiFactory(await pg.CreateDatabaseAsync());

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---- helpers ----

    /// <summary>A client of a derived factory (CreateVersionedClient is not reachable after WithWebHostBuilder).</summary>
    private static HttpClient Client(WebApplicationFactory<Program> factory, SignedInUser user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-EasyStock-App-Version", "1.0.0");
        client.DefaultRequestHeaders.Add("X-EasyStock-Schema-Version", "10");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    private HttpClient ClientAs(SignedInUser user) => _factory.ClientFor(user);

    private async Task<HttpClient> ClientAs(Role role) => _factory.ClientFor(await _factory.SignInAsync(role));

    private async Task<T> WithDb<T>(Func<EasyStockDbContext, Task<T>> action)
    {
        _factory.CreateClient(); // starts the host, which migrates the database
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<EasyStockDbContext>());
    }

    private Task<long> Cursor() => WithDb(db => db.Database
        .SqlQueryRaw<long>("SELECT COALESCE(pg_sequence_last_value('server_seq'), 0) AS \"Value\"")
        .SingleAsync(Ct));

    private Task Insert(params object[] rows) => WithDb(async db =>
    {
        foreach (var row in rows)
        {
            db.Add(row);
            await db.SaveChangesAsync(Ct); // one by one: the seq order is the order of the arguments
        }
        return 0;
    });

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private sealed record Fixture(Category Category, Location Location, Article Article);

    private static Fixture NewFixture()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = Unique("cat"), CreatedAt = 1, UpdatedAt = 1 };
        var location = new Location { Id = Guid.NewGuid(), Name = Unique("loc"), CreatedAt = 1, UpdatedAt = 1 };
        var article = new Article
        {
            Id = Guid.NewGuid(), Name = Unique("art"), CategoryId = category.Id, Threshold = 5000,
            Attributes = """{"diameter":8,"rope_type":"static"}""", CreatedAt = 1762859000000, UpdatedAt = 1762859000000,
        };
        return new Fixture(category, location, article);
    }

    private static StockMovement Movement(Fixture f, long quantity, Guid? author = null) => new()
    {
        Id = Guid.NewGuid(), ArticleId = f.Article.Id, LocationId = f.Location.Id, Quantity = quantity,
        Reason = "consumption", Comment = "secret comment", OccurredAt = 1762859000000, CreatedAt = 1762859000000,
        CreatedBy = author ?? Guid.NewGuid(), DeviceId = Guid.NewGuid(),
    };

    private static async Task<JsonDocument> Pull(HttpClient client, long since, int? limit = null)
    {
        var url = $"/api/v1/sync/pull?since={since}" + (limit is null ? "" : $"&limit={limit}");
        var response = await client.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private static IEnumerable<JsonElement> Rows(JsonDocument page, string table) =>
        page.RootElement.GetProperty("rows").TryGetProperty(table, out var rows) ? rows.EnumerateArray() : [];

    private static IEnumerable<(string Table, JsonElement Row)> AllRows(JsonDocument page) =>
        page.RootElement.GetProperty("rows").EnumerateObject()
            .SelectMany(t => t.Value.EnumerateArray().Select(r => (t.Name, r)));

    /// <summary>Pulls page after page from <paramref name="since"/> until every id of <paramref name="ids"/> came.</summary>
    private static async Task<List<(string Table, JsonElement Row)>> PullAll(
        HttpClient client, long since, int limit, IReadOnlySet<string> ids)
    {
        var seen = new List<(string, JsonElement)>();
        for (var page = 0; page < 1000 && !ids.IsSubsetOf(seen.Select(r => r.Item2.GetProperty("id").GetString()!)); page++)
        {
            using var doc = await Pull(client, since, limit);
            var root = doc.RootElement;
            var rows = AllRows(doc).ToList();
            var next = root.GetProperty("next").GetInt64();
            Assert.True(rows.Count <= limit);
            foreach (var (_, row) in rows)
            {
                var seq = row.GetProperty("serverSeq").GetInt64();
                Assert.InRange(seq, since + 1, next); // the page is exactly the window (since, next]
            }
            if (!root.GetProperty("hasMore").GetBoolean() && rows.Count == 0) Assert.Equal(since, next);
            Assert.True(next >= since);
            seen.AddRange(rows.Select(r => (r.Item1, r.Item2.Clone())));
            since = next;
        }
        return seen;
    }

    // ---- permission ----

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var response = await _factory.ClientFor(null).GetAsync("/api/v1/sync/pull?since=0", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("unauthorized", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void Pull_requires_ViewStock()
    {
        // Every role has ViewStock, so no role gets 403 today: check the gate itself.
        var method = typeof(SyncPullController).GetMethod(nameof(SyncPullController.Get))!;
        var gate = Assert.Single(method.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.Equal(Permission.ViewStock, gate.Permission);
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.Member)]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Guest)]
    public async Task Every_role_can_pull(Role role)
    {
        using var doc = await Pull(await ClientAs(role), await Cursor(), 1);
        Assert.True(doc.RootElement.TryGetProperty("next", out _));
    }

    // ---- validation ----

    [Theory]
    [InlineData("since=0&limit=0")]
    [InlineData("since=0&limit=-1")]
    [InlineData("since=0&limit=2001")]
    [InlineData("since=-1")]
    public async Task Out_of_range_query_gives_400_validation_failed(string query)
    {
        var response = await (await ClientAs(Role.Viewer)).GetAsync($"/api/v1/sync/pull?{query}", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("validation-failed", doc.RootElement.GetProperty("type").GetString());
        Assert.True(doc.RootElement.GetProperty("errors").EnumerateObject().Any());
    }

    [Fact]
    public async Task Non_numeric_query_gives_400()
    {
        var response = await (await ClientAs(Role.Viewer)).GetAsync("/api/v1/sync/pull?since=abc", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Default_limit_is_500_and_2000_is_allowed()
    {
        var locations = Enumerable.Range(0, 501)
            .Select(_ => new Location { Id = Guid.NewGuid(), Name = Unique("many"), CreatedAt = 1, UpdatedAt = 1 }).ToList();
        var since = await Cursor();
        await WithDb(db => { db.AddRange(locations); return db.SaveChangesAsync(Ct); });

        var client = await ClientAs(Role.Viewer);
        using var @default = await Pull(client, since);
        Assert.Equal(500, AllRows(@default).Count());
        Assert.True(@default.RootElement.GetProperty("hasMore").GetBoolean());

        using var max = await Pull(client, since, 2000);
        var ids = AllRows(max).Select(r => r.Row.GetProperty("id").GetString()).ToHashSet();
        Assert.True(AllRows(max).Count() <= 2000);
        Assert.All(locations, l => Assert.Contains(l.Id.ToString(), ids));
    }

    // ---- shape ----

    [Fact]
    public async Task Rows_use_the_app_wire_format()
    {
        var f = NewFixture();
        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(), Key = Unique("k"), Label = "Type", DataType = "enum", Options = """["a","b"]""",
            CreatedAt = 1, UpdatedAt = 2,
        };
        var since = await Cursor();
        await Insert(f.Category, f.Location, f.Article, definition, Movement(f, -2000));

        using var doc = await Pull(await ClientAs(Role.Viewer), since, 2000);
        var root = doc.RootElement;
        foreach (var name in new[] { "rows", "users", "alerts", "next", "hasMore", "datasetEpoch" })
            Assert.True(root.TryGetProperty(name, out _), $"missing {name}");
        Assert.True(Guid.TryParse(root.GetProperty("datasetEpoch").GetString(), out _));

        var article = Rows(doc, "article").Single(r => r.GetProperty("id").GetString() == f.Article.Id.ToString());
        Assert.Equal(f.Article.Name, article.GetProperty("name").GetString());
        Assert.Equal(f.Category.Id.ToString(), article.GetProperty("categoryId").GetString());
        Assert.Equal(5000, article.GetProperty("threshold").GetInt64());
        Assert.Equal(1762859000000, article.GetProperty("updatedAt").GetInt64());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("deletedAt").ValueKind); // nulls are written
        Assert.Equal(JsonValueKind.Null, article.GetProperty("imageSha256").ValueKind);
        Assert.False(article.TryGetProperty("image_sha256", out _));
        Assert.Equal("static", article.GetProperty("attributes").GetProperty("rope_type").GetString()); // jsonb as JSON, keys kept
        Assert.Equal(8, article.GetProperty("attributes").GetProperty("diameter").GetInt32());

        var options = Rows(doc, "attribute_definition").Single(r => r.GetProperty("id").GetString() == definition.Id.ToString())
            .GetProperty("options");
        Assert.Equal(new[] { "a", "b" }, options.EnumerateArray().Select(o => o.GetString()));

        var movement = Assert.Single(Rows(doc, "stock_movement"), r => r.GetProperty("articleId").GetString() == f.Article.Id.ToString());
        Assert.Equal(-2000, movement.GetProperty("quantity").GetInt64());
        Assert.Equal("consumption", movement.GetProperty("reason").GetString());
        Assert.Equal(1762859000000, movement.GetProperty("occurredAt").GetInt64());
    }

    [Fact]
    public async Task Empty_page_keeps_the_cursor()
    {
        using var doc = await Pull(await ClientAs(Role.Member), long.MaxValue - 1, 10);
        Assert.Empty(doc.RootElement.GetProperty("rows").EnumerateObject());
        Assert.Equal(long.MaxValue - 1, doc.RootElement.GetProperty("next").GetInt64());
        Assert.False(doc.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Soft_deleted_rows_are_pulled()
    {
        var location = new Location { Id = Guid.NewGuid(), Name = Unique("gone"), CreatedAt = 1, UpdatedAt = 2, DeletedAt = 2 };
        var since = await Cursor();
        await Insert(location);
        var rows = await PullAll(await ClientAs(Role.Guest), since, 500, new HashSet<string> { location.Id.ToString() });
        var row = rows.Single(r => r.Row.GetProperty("id").GetString() == location.Id.ToString());
        Assert.Equal("location", row.Table);
        Assert.Equal(2, row.Row.GetProperty("deletedAt").GetInt64());
    }

    // ---- paging ----

    [Fact]
    public async Task Small_pages_return_every_row_once_in_seq_order_across_tables()
    {
        var f = NewFixture();
        var movements = Enumerable.Range(1, 5).Select(i => Movement(f, i * 1000)).ToArray();
        var item = new Item { Id = Guid.NewGuid(), ArticleId = f.Article.Id, LocationId = f.Location.Id, CreatedAt = 1, UpdatedAt = 1 };
        object[] inserted = [f.Category, f.Location, f.Article, movements[0], movements[1], item, movements[2], movements[3], movements[4]];
        var since = await Cursor();
        await Insert(inserted);
        // An update moves a row to a new seq: the category comes again, after the movements.
        await WithDb(async db =>
        {
            db.Attach(f.Category);
            f.Category.UpdatedAt = 99;
            return await db.SaveChangesAsync(Ct);
        });

        var ids = inserted.Select(r => r switch
        {
            Category c => c.Id, Location l => l.Id, Article a => a.Id, Item i => i.Id, StockMovement m => m.Id,
            _ => throw new InvalidOperationException(),
        }).Select(id => id.ToString()).ToHashSet();
        foreach (var limit in new[] { 1, 2, 3 })
        {
            var rows = await PullAll(await ClientAs(Role.Viewer), since, limit, ids);
            var mine = rows.Where(r => ids.Contains(r.Row.GetProperty("id").GetString()!)).ToList();

            // Each row once; the updated category only in its last version.
            Assert.Equal(ids.Count, mine.Count);
            Assert.Equal(ids.Count, mine.Select(r => r.Row.GetProperty("id").GetString()).Distinct().Count());

            // Pages are consecutive windows (checked by PullAll), and each table is in seq order within a page.
            var seqs = rows.Select(r => r.Row.GetProperty("serverSeq").GetInt64()).ToList();
            Assert.Equal(seqs.Count, seqs.Distinct().Count());
            foreach (var table in rows.GroupBy(r => r.Table))
            {
                var tableSeqs = table.Select(r => r.Row.GetProperty("serverSeq").GetInt64()).ToList();
                Assert.Equal(tableSeqs.Order(), tableSeqs);
            }

            var category = mine.Single(r => r.Table == "category");
            Assert.Equal(f.Category.Id.ToString(), category.Row.GetProperty("id").GetString());
            Assert.Equal(99, category.Row.GetProperty("updatedAt").GetInt64());
            Assert.Equal(f.Category.ServerSeq, category.Row.GetProperty("serverSeq").GetInt64());
            Assert.Equal(
                movements.Select(m => m.Id.ToString()),
                mine.Where(r => r.Table == "stock_movement").Select(r => r.Row.GetProperty("id").GetString()));
            Assert.Equal("item", mine.Single(r => r.Row.GetProperty("id").GetString() == item.Id.ToString()).Table);
        }
    }

    [Fact]
    public async Task A_full_page_says_hasMore_and_next_is_its_last_seq()
    {
        var f = NewFixture();
        var since = await Cursor();
        await Insert(f.Category, f.Location, f.Article, Movement(f, 1000));

        using var doc = await Pull(await ClientAs(Role.Viewer), since, 2);
        var rows = AllRows(doc).ToList();
        Assert.Equal(2, rows.Count);
        Assert.True(doc.RootElement.GetProperty("hasMore").GetBoolean());
        var seqs = rows.Select(r => r.Row.GetProperty("serverSeq").GetInt64()).ToList();
        Assert.Equal(seqs.Max(), doc.RootElement.GetProperty("next").GetInt64());
        // Nothing else committed can sit before our first row: it was inserted right after the cursor was read.
        Assert.Equal(f.Category.ServerSeq, seqs.Min());
    }

    // ---- Guest filter ----

    [Fact]
    public async Task Guest_pull_has_no_author_device_comment_review_user_or_alert()
    {
        var f = NewFixture();
        var review = new StockReview { Id = Guid.NewGuid(), LocationId = f.Location.Id, StartedAt = 1, CreatedAt = 1, UpdatedAt = 1 };
        var line = new StockReviewLine
        {
            Id = Guid.NewGuid(), ReviewId = review.Id, ArticleId = f.Article.Id, LocationId = f.Location.Id, ExpectedQuantity = 3000,
            PendingItem = """{"serial_number":"SN-1","status":"available"}""", CreatedAt = 1, UpdatedAt = 1,
        };
        var movements = new[] { Movement(f, 5000), Movement(f, -1500), Movement(f, -250) };
        var alert = new LowStockAlert { Id = Guid.NewGuid(), ArticleId = f.Article.Id, CrossedAt = 1762859000000, Stock = 3250, CausedByUserId = null };
        var ids = new HashSet<string> { review.Id.ToString(), line.Id.ToString(), alert.Id.ToString() };
        ids.UnionWith(movements.Select(m => m.Id.ToString()));

        var since = await Cursor();
        await Insert([f.Category, f.Location, f.Article, review, line, .. movements, alert]);
        var until = alert.ServerSeq; // our last row

        async Task<(List<(string Table, JsonElement Row)> Rows, List<JsonElement> Users, List<JsonElement> Alerts)> PullWindow(Role role)
        {
            var client = await ClientAs(role);
            var rows = new List<(string, JsonElement)>();
            var users = new List<JsonElement>();
            var alerts = new List<JsonElement>();
            for (var cursor = since; cursor < until;)
            {
                using var doc = await Pull(client, cursor, 2); // small pages: the cursor advances past filtered rows
                rows.AddRange(AllRows(doc).Select(r => (r.Table, r.Row.Clone())));
                users.AddRange(doc.RootElement.GetProperty("users").EnumerateArray().Select(u => u.Clone()));
                alerts.AddRange(doc.RootElement.GetProperty("alerts").EnumerateArray().Select(a => a.Clone()));
                var next = doc.RootElement.GetProperty("next").GetInt64();
                Assert.True(next > cursor, "the cursor advances");
                cursor = next;
            }
            return (rows, users, alerts);
        }

        var guest = await PullWindow(Role.Guest);
        Assert.DoesNotContain(guest.Rows, r => r.Table is "stock_review" or "stock_review_line");
        Assert.Empty(guest.Users);
        Assert.Empty(guest.Alerts);
        var guestMovements = guest.Rows.Where(r => r.Table == "stock_movement").Select(r => r.Row).ToList();
        Assert.NotEmpty(guestMovements);
        foreach (var m in guestMovements)
            foreach (var field in new[] { "createdBy", "deviceId", "comment" })
                Assert.False(m.TryGetProperty(field, out _), $"a Guest got {field}");

        var member = await PullWindow(Role.Member);
        Assert.Contains(member.Rows, r => r.Table == "stock_review" && r.Row.GetProperty("id").GetString() == review.Id.ToString());
        var memberLine = member.Rows.Single(r => r.Table == "stock_review_line" && r.Row.GetProperty("id").GetString() == line.Id.ToString());
        Assert.Equal("SN-1", memberLine.Row.GetProperty("pendingItem").GetProperty("serial_number").GetString());
        var memberMovement = member.Rows.First(r => r.Row.GetProperty("id").GetString() == movements[0].Id.ToString()).Row;
        Assert.Equal(movements[0].CreatedBy.ToString(), memberMovement.GetProperty("createdBy").GetString());
        Assert.Equal(movements[0].DeviceId.ToString(), memberMovement.GetProperty("deviceId").GetString());
        Assert.Equal("secret comment", memberMovement.GetProperty("comment").GetString());
        Assert.Contains(member.Alerts, a => a.GetProperty("id").GetString() == alert.Id.ToString());

        // The stock a Guest computes is the one a Member computes.
        long Stock(IEnumerable<(string Table, JsonElement Row)> rows) => rows
            .Where(r => r.Table == "stock_movement" && r.Row.GetProperty("articleId").GetString() == f.Article.Id.ToString())
            .Sum(r => r.Row.GetProperty("quantity").GetInt64());
        Assert.Equal(3250, Stock(member.Rows));
        Assert.Equal(Stock(member.Rows), Stock(guest.Rows));
    }

    // ---- anonymized authors ----

    [Fact]
    public async Task Clearing_the_author_of_a_movement_bumps_its_seq_so_phones_pull_it()
    {
        var f = NewFixture();
        var movement = Movement(f, 1000);
        await Insert(f.Category, f.Location, f.Article, movement);
        var since = await Cursor();
        var before = await WithDb(db => db.StockMovements.Where(m => m.Id == movement.Id).Select(m => m.ServerSeq).SingleAsync(Ct));

        // An author change (for example after a user is deleted)
        await WithDb(db => db.Database.ExecuteSqlAsync(
            $"UPDATE stock_movement SET created_by = NULL, device_id = NULL WHERE id = {movement.Id}", Ct));

        using var doc = await Pull(await ClientAs(Role.Member), since);
        var row = AllRows(doc).Single(r => r.Row.GetProperty("id").GetString() == movement.Id.ToString());
        Assert.True(row.Row.GetProperty("serverSeq").GetInt64() > before);
        Assert.True(!row.Row.TryGetProperty("createdBy", out var createdBy) || createdBy.ValueKind is JsonValueKind.Null);
        Assert.True(!row.Row.TryGetProperty("deviceId", out var deviceId) || deviceId.ValueKind is JsonValueKind.Null);
        Assert.Equal(1000, row.Row.GetProperty("quantity").GetInt64());
    }

    // ---- alerts ----

    [Fact]
    public async Task Alerts_go_to_recipients_but_not_to_the_author()
    {
        var f = NewFixture();
        var author = await _factory.SignInAsync(Role.Member);
        var alert = new LowStockAlert { Id = Guid.NewGuid(), ArticleId = f.Article.Id, CrossedAt = 1762859000000, Stock = 4000, CausedByUserId = author.UserId };
        var since = await Cursor();
        await Insert(f.Category, f.Location, f.Article, alert);

        async Task<JsonElement?> AlertFor(SignedInUser user)
        {
            var client = ClientAs(user);
            for (var cursor = since; cursor < alert.ServerSeq;)
            {
                using var doc = await Pull(client, cursor, 2000);
                foreach (var a in doc.RootElement.GetProperty("alerts").EnumerateArray())
                    if (a.GetProperty("id").GetString() == alert.Id.ToString()) return a.Clone();
                var next = doc.RootElement.GetProperty("next").GetInt64();
                Assert.True(next > cursor, "the cursor advances");
                cursor = next;
            }
            return null;
        }

        var forMember = await AlertFor(await _factory.SignInAsync(Role.Member));
        Assert.NotNull(forMember);
        Assert.Equal(f.Article.Id.ToString(), forMember.Value.GetProperty("articleId").GetString());
        Assert.Equal(1762859000000, forMember.Value.GetProperty("crossedAt").GetInt64());
        Assert.Equal(4000, forMember.Value.GetProperty("stock").GetInt64());
        Assert.Equal(4, forMember.Value.EnumerateObject().Count()); // id, articleId, crossedAt, stock

        Assert.NotNull(await AlertFor(await _factory.SignInAsync(Role.Admin)));
        Assert.Null(await AlertFor(author));
        Assert.Null(await AlertFor(await _factory.SignInAsync(Role.Guest)));
        // A full account flagged as a Guest kind (pending promotion) keeps Guest rights.
        Assert.Null(await AlertFor(await _factory.SignInAsync(Role.Member, guestKind: true)));
    }

    [Fact]
    public async Task Alerts_follow_users_alerts_enabled_over_the_role_default()
    {
        var f = NewFixture();
        var author = await _factory.SignInAsync(Role.Member);
        var alert = new LowStockAlert { Id = Guid.NewGuid(), ArticleId = f.Article.Id, CrossedAt = 1762859000000, Stock = 4000, CausedByUserId = author.UserId };
        var since = await Cursor();
        await Insert(f.Category, f.Location, f.Article, alert);

        async Task<bool> Gets(SignedInUser user)
        {
            using var doc = await Pull(ClientAs(user), since, 2000);
            return doc.RootElement.GetProperty("alerts").EnumerateArray()
                .Any(a => a.GetProperty("id").GetString() == alert.Id.ToString());
        }

        async Task SetAlerts(SignedInUser user, string json)
        {
            var response = await ClientAs(user).PatchAsync("/api/v1/me",
                new StringContent($$"""{"alertsEnabled":{{json}}}""", System.Text.Encoding.UTF8, "application/json"), Ct);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        }

        var viewer = await _factory.SignInAsync(Role.Viewer);
        Assert.False(await Gets(viewer));
        await SetAlerts(viewer, "true");
        Assert.True(await Gets(viewer));
        await SetAlerts(viewer, "null");
        Assert.False(await Gets(viewer));

        var member = await _factory.SignInAsync(Role.Member);
        Assert.True(await Gets(member));
        await SetAlerts(member, "false");
        Assert.False(await Gets(member));
        await SetAlerts(member, "null");
        Assert.True(await Gets(member));

        var admin = await _factory.SignInAsync(Role.Admin);
        Assert.True(await Gets(admin));
        await SetAlerts(admin, "false");
        Assert.False(await Gets(admin));

        Assert.False(await Gets(await _factory.SignInAsync(Role.Guest)));
    }

    [Theory]
    [InlineData(Role.Admin, false, null, true)]
    [InlineData(Role.Admin, false, false, false)]
    [InlineData(Role.Member, false, null, true)]
    [InlineData(Role.Member, false, false, false)]
    [InlineData(Role.Viewer, false, null, false)]
    [InlineData(Role.Viewer, false, true, true)]
    [InlineData(Role.Guest, true, true, false)]
    [InlineData(Role.Member, true, true, false)]
    public void Recipient_rule(Role role, bool isGuest, bool? alertsEnabled, bool expected) =>
        Assert.Equal(expected, EasyStock.Server.Helpers.AlertRecipientsHelper.IsRecipient(role, isGuest, alertsEnabled));

    // ---- users ----

    private sealed class FakeUserDirectory(List<PullUserChange> changes, Dictionary<Guid, string> names) : IPullUserDirectoryHandler
    {
        public Task<IReadOnlyList<PullUserChange>> ChangedSinceAsync(long since, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PullUserChange>>(
                changes.Where(c => c.ServerSeq > since).OrderBy(c => c.ServerSeq).Take(take).ToList());

        public Task<IReadOnlyList<(Guid Id, string DisplayName)>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(Guid, string)>>(
                ids.Where(names.ContainsKey).Select(id => (id, names[id])).ToList());
    }

    [Fact]
    public async Task Users_are_the_authors_of_the_movements_plus_the_users_in_the_window()
    {
        var f = NewFixture();
        var authorId = Guid.NewGuid();
        var renamedId = Guid.NewGuid();
        var movement = Movement(f, 1000, authorId);
        var since = await Cursor();
        await Insert(f.Category, f.Location, f.Article, movement);
        var renamedSeq = await WithDb(db => db.Database.SqlQueryRaw<long>("SELECT nextval('server_seq') AS \"Value\"").SingleAsync(Ct));
        var directory = new FakeUserDirectory(
            [new PullUserChange(renamedId, "Renamed", renamedSeq)],
            new Dictionary<Guid, string> { [authorId] = "Jean", [renamedId] = "Renamed" });

        using var withDirectory = _factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IPullUserDirectoryHandler>();
                s.AddSingleton<IPullUserDirectoryHandler>(directory);
            }));
        async Task<HttpClient> ClientWith(Role role) => Client(withDirectory, await _factory.SignInAsync(role));

        // The movement's page names its author.
        using (var doc = await Pull(await ClientWith(Role.Viewer), movement.ServerSeq - 1, 1))
        {
            Assert.Equal(movement.Id.ToString(), Assert.Single(AllRows(doc)).Row.GetProperty("id").GetString());
            var user = Assert.Single(doc.RootElement.GetProperty("users").EnumerateArray());
            Assert.Equal(authorId.ToString(), user.GetProperty("id").GetString());
            Assert.Equal("Jean", user.GetProperty("displayName").GetString());
        }

        // A window with only a user change still advances the cursor and delivers the name.
        using (var doc = await Pull(await ClientWith(Role.Viewer), renamedSeq - 1, 1))
        {
            Assert.Empty(AllRows(doc));
            Assert.Equal(renamedSeq, doc.RootElement.GetProperty("next").GetInt64());
            var user = Assert.Single(doc.RootElement.GetProperty("users").EnumerateArray());
            Assert.Equal(renamedId.ToString(), user.GetProperty("id").GetString());
        }

        // Guests get no names.
        using (var doc = await Pull(await ClientWith(Role.Guest), movement.ServerSeq - 1, 1))
            Assert.Empty(doc.RootElement.GetProperty("users").EnumerateArray());
        using (var doc = await Pull(await ClientWith(Role.Guest), renamedSeq - 1, 1))
        {
            Assert.Empty(doc.RootElement.GetProperty("users").EnumerateArray());
            Assert.Equal(renamedSeq, doc.RootElement.GetProperty("next").GetInt64());
        }
    }

    [Fact]
    public async Task Users_come_from_the_users_table_and_a_deleted_user_is_named_Deleted_user()
    {
        var since = await Cursor();
        var kept = await _factory.SignInAsync(Role.Member);
        var deleted = await _factory.SignInAsync(Role.Member);
        await WithDb(db => db.Users.Where(u => u.Id == deleted.UserId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.DeletedAt, DateTimeOffset.UnixEpoch), Ct));

        var client = await ClientAs(Role.Viewer);
        var users = new Dictionary<string, string>();
        for (var cursor = since; ;)
        {
            using var doc = await Pull(client, cursor, 2000);
            foreach (var u in doc.RootElement.GetProperty("users").EnumerateArray())
                users[u.GetProperty("id").GetString()!] = u.GetProperty("displayName").GetString()!;
            var next = doc.RootElement.GetProperty("next").GetInt64();
            if (!doc.RootElement.GetProperty("hasMore").GetBoolean()) break;
            Assert.True(next > cursor, "the cursor advances while there is more");
            cursor = next;
        }
        Assert.Equal("Test user", users[kept.UserId.ToString()]);
        Assert.Equal("Deleted user", users[deleted.UserId.ToString()]);

        // Guests get no names.
        using var guestDoc = await Pull(await ClientAs(Role.Guest), since, 2000);
        Assert.Empty(guestDoc.RootElement.GetProperty("users").EnumerateArray());
    }

    // ---- consistency ----

    [Fact]
    public async Task Concurrent_writes_never_make_a_pull_skip_a_row()
    {
        // Each write commits a category then a location, so the earlier-read table gets the lower seq: without the
        // snapshot, a pull could read category before the commit and location after it, and skip the category.
        var since = await Cursor();
        var written = new System.Collections.Concurrent.ConcurrentBag<(Guid Id, long Seq)>();
        var writersDone = false;

        async Task Writer()
        {
            for (var i = 0; i < 100; i++)
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                await using var tx = await db.Database.BeginTransactionAsync(Ct);
                var category = new Category { Id = Guid.NewGuid(), Name = Unique("race"), CreatedAt = 1, UpdatedAt = 1 };
                var location = new Location { Id = Guid.NewGuid(), Name = Unique("race"), CreatedAt = 1, UpdatedAt = 1 };
                db.Add(category);
                await db.SaveChangesAsync(Ct);
                db.Add(location);
                await db.SaveChangesAsync(Ct);
                await tx.CommitAsync(Ct);
                written.Add((category.Id, category.ServerSeq));
                written.Add((location.Id, location.ServerSeq));
            }
        }

        // The pullers poll in a tight loop: without the per-session limit (120 per minute), which is not tested here.
        using var noLimit = _factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.PostConfigure<RateLimiterOptions>(o => o.GlobalLimiter = null)));

        async Task<List<string>> Puller()
        {
            var client = Client(noLimit, await _factory.SignInAsync(Role.Viewer));
            var seen = new List<string>();
            var cursor = since;
            while (true)
            {
                var done = Volatile.Read(ref writersDone);
                using var doc = await Pull(client, cursor, 2000); // caught up: the page reaches the newest commits
                seen.AddRange(AllRows(doc).Select(r => r.Row.GetProperty("id").GetString()!));
                cursor = doc.RootElement.GetProperty("next").GetInt64();
                if (done && cursor >= written.Max(w => w.Seq)) return seen;
            }
        }

        _factory.CreateClient();
        var pullers = Enumerable.Range(0, 4).Select(_ => Task.Run(Puller, Ct)).ToArray();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(Writer, Ct)));
        Volatile.Write(ref writersDone, true);

        foreach (var seen in await Task.WhenAll(pullers))
        {
            var counts = seen.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
            foreach (var (id, _) in written)
                Assert.True(counts.GetValueOrDefault(id.ToString()) == 1, $"row {id} pulled {counts.GetValueOrDefault(id.ToString())} times");
        }
    }

    [Fact]
    public async Task Pull_does_not_wait_for_the_sync_lock()
    {
        var client = await ClientAs(Role.Viewer); // before the lock: signing in writes a users row, which takes it
        var since = await Cursor();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        await db.AcquireSyncLockAsync(Ct); // a push in progress

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var response = await client.GetAsync($"/api/v1/sync/pull?since={since}&limit=1", cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tx.RollbackAsync(Ct);
    }
}

using System.Net;
using System.Text.Json.Nodes;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EasyStock.Server.Tests.Infrastructure.SyncPushKit;

namespace EasyStock.Server.Tests;

public sealed class SyncPushTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private static Category NewCategory() => new() { Id = Guid.NewGuid(), Name = Unique("cat"), CreatedAt = 1, UpdatedAt = 10 };
    private static Location NewLocation(Guid? parent = null) =>
        new() { Id = Guid.NewGuid(), Name = Unique("loc"), ParentId = parent, CreatedAt = 1, UpdatedAt = 10 };
    private static Article NewArticle(long? threshold = null, string tracking = "quantity") =>
        new() { Id = Guid.NewGuid(), Name = Unique("art"), Tracking = tracking, Threshold = threshold, CreatedAt = 1, UpdatedAt = 10 };
    private static StockMovement NewMovement(Article a, Location l, long quantity, string reason = "purchase") =>
        new() { Id = Guid.NewGuid(), ArticleId = a.Id, LocationId = l.Id, Quantity = quantity, Reason = reason, OccurredAt = 5, CreatedAt = 5 };
    private static AttributeDefinition NewAttribute() =>
        new() { Id = Guid.NewGuid(), Key = Unique("key"), Label = "Length", DataType = "number", CreatedAt = 1, UpdatedAt = 10 };

    private Task<T> Row<T>(Guid id) where T : class =>
        _factory.QueryAsync(db => db.Set<T>().AsNoTracking().SingleAsync(e => EF.Property<Guid>(e, "Id") == id, Ct));

    // ---- Roles ----

    [Theory]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Guest)]
    public async Task Viewer_and_guest_get_403_for_the_whole_request(Role role)
    {
        var category = NewCategory();
        var answer = await _factory.PushClient(await _factory.SignInAsync(role)).PushAsync((SyncTablesCategory, Wire(category)));

        Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
        Assert.Equal("not-allowed", (string?)answer.Json["type"]);
        Assert.False(await _factory.QueryAsync(db => db.Categories.AnyAsync(c => c.Id == category.Id, Ct)));
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var answer = await _factory.PushClient(null).PushAsync((SyncTablesCategory, Wire(NewCategory())));
        Assert.Equal(HttpStatusCode.Unauthorized, answer.Status);
    }

    [Fact]
    public async Task Member_admin_only_rows_are_rejected_not_allowed_one_by_one()
    {
        var category = NewCategory();
        var hall = NewLocation();
        var shelf = NewLocation();
        await _factory.SeedAsync(category, hall, shelf);

        var renamed = Edit(category); renamed["name"] = Unique("renamed");
        var reparented = Edit(shelf); reparented["parentId"] = hall.Id.ToString();
        var deleted = Edit(hall); deleted["deletedAt"] = 50;
        var newCategory = NewCategory();
        var attribute = NewAttribute();
        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(
            ("category", renamed), ("category", Wire(newCategory)), ("attribute_definition", Wire(attribute)),
            ("location", reparented), ("location", deleted));

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        var edit = answer.Result("category", category.Id);
        Assert.Equal("rejected", (string?)edit["status"]);
        Assert.Equal("not-allowed", (string?)edit["reason"]);
        Assert.Equal(category.Name, (string?)edit["row"]!["name"]); // the server's version
        Assert.Equal("accepted", (string?)answer.Result("category", newCategory.Id)["status"]);
        var attr = answer.Result("attribute_definition", attribute.Id);
        Assert.Equal("not-allowed", (string?)attr["reason"]);
        Assert.Empty(attr["row"]!.AsObject()); // no server version
        Assert.Equal("not-allowed", (string?)answer.Result("location", shelf.Id)["reason"]);
        Assert.Equal("not-allowed", (string?)answer.Result("location", hall.Id)["reason"]);

        Assert.Null((await Row<Location>(shelf.Id)).ParentId);
        Assert.Null((await Row<Location>(hall.Id)).DeletedAt);
        Assert.Equal(category.Name, (await Row<Category>(category.Id)).Name);
    }

    [Fact]
    public async Task Member_renames_a_location_and_sets_its_tag()
    {
        var shelf = NewLocation();
        await _factory.SeedAsync(shelf);
        var row = Edit(shelf);
        row["name"] = Unique("renamed");
        row["barcode"] = Unique("tag");

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(("location", row));

        Assert.Equal("accepted", (string?)answer.Result("location", shelf.Id)["status"]);
        var saved = await Row<Location>(shelf.Id);
        Assert.Equal((string?)row["name"], saved.Name);
        Assert.Equal((string?)row["barcode"], saved.Barcode);
    }

    [Fact]
    public async Task Admin_reparents_deletes_and_edits_categories()
    {
        var category = NewCategory();
        var hall = NewLocation();
        var shelf = NewLocation();
        await _factory.SeedAsync(category, hall, shelf);
        var renamed = Edit(category); renamed["name"] = Unique("renamed");
        var reparented = Edit(shelf); reparented["parentId"] = hall.Id.ToString();
        var deleted = Edit(hall); deleted["deletedAt"] = 50;

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Admin)).PushAsync(
            ("category", renamed), ("location", reparented), ("location", deleted));

        Assert.All(answer.Json["results"]!.AsArray(), r => Assert.Equal("accepted", (string?)r!["status"]));
        Assert.Equal(hall.Id, (await Row<Location>(shelf.Id)).ParentId);
        Assert.Equal(50, (await Row<Location>(hall.Id)).DeletedAt);
    }

    private const string SyncTablesCategory = "category";

    // ---- Batches ----

    [Fact]
    public async Task Same_batch_again_returns_the_same_bytes_and_applies_nothing()
    {
        var (article, location) = (NewArticle(), NewLocation());
        await _factory.SeedAsync(article, location);
        var movement = NewMovement(article, location, 1000);
        var category = NewCategory();
        var body = Body(Guid.NewGuid(), ("category", Wire(category)), ("stock_movement", Wire(movement)));
        var client = _factory.PushClient(await _factory.SignInAsync(Role.Member));

        var first = await client.PushAsync(body);
        var seq = (await Row<Category>(category.Id)).ServerSeq;
        var again = await client.PushAsync(body);

        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(first.Text, again.Text);
        Assert.Equal(seq, (await Row<Category>(category.Id)).ServerSeq);
        Assert.Equal(1, await _factory.QueryAsync(db => db.StockMovements.CountAsync(m => m.ArticleId == article.Id, Ct)));
    }

    [Fact]
    public async Task Same_batch_by_another_user_is_409()
    {
        var body = Body(Guid.NewGuid(), ("category", Wire(NewCategory())));
        await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(body);

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(body);

        Assert.Equal(HttpStatusCode.Conflict, answer.Status);
        Assert.Equal("batch-id-reused", (string?)answer.Json["type"]);
    }

    [Fact]
    public async Task Same_batch_twice_at_once_is_applied_once()
    {
        var (article, location) = (NewArticle(), NewLocation());
        await _factory.SeedAsync(article, location);
        var body = Body(Guid.NewGuid(), ("stock_movement", Wire(NewMovement(article, location, 1000))));
        var user = await _factory.SignInAsync(Role.Member);
        var a = _factory.PushClient(user);
        var b = _factory.PushClient(user);

        var answers = await Task.WhenAll(a.PushAsync(body), b.PushAsync(body));

        Assert.All(answers, x => Assert.Equal(HttpStatusCode.OK, x.Status));
        Assert.Equal(answers[0].Text, answers[1].Text);
        Assert.Equal(1, await _factory.QueryAsync(db => db.StockMovements.CountAsync(m => m.ArticleId == article.Id, Ct)));
    }

    [Fact]
    public async Task Bad_batch_id_is_400_and_too_many_rows_413()
    {
        var client = _factory.PushClient(await _factory.SignInAsync(Role.Member));
        var bad = Body(Guid.NewGuid());
        bad["batchId"] = "not-a-uuid";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PushAsync(bad)).Status);

        var big = Body(Guid.NewGuid());
        big["rows"] = new JsonObject { ["category"] = new JsonArray(Enumerable.Range(0, 5001).Select(_ => (JsonNode?)new JsonObject()).ToArray()) };
        var answer = await client.PushAsync(big);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, answer.Status);
        Assert.Equal("batch-too-large", (string?)answer.Json["type"]);
    }

    [Fact]
    public async Task Body_over_10_MB_is_413()
    {
        var body = Body(Guid.NewGuid());
        body["padding"] = new string('x', 10 * 1024 * 1024);

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, answer.Status);
        Assert.Equal("batch-too-large", (string?)answer.Json["type"]);
    }

    [Fact]
    public async Task Location_made_its_own_parent_keeps_its_parent()
    {
        var hall = NewLocation();
        var shelf = NewLocation(hall.Id);
        await _factory.SeedAsync(hall, shelf);
        var row = Edit(shelf);
        row["parentId"] = shelf.Id.ToString();

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Admin)).PushAsync(("location", row));

        var result = answer.Result("location", shelf.Id);
        Assert.Equal("LOCATION_PARENT_KEPT", (string?)result["conflict"]);
        Assert.Equal(hall.Id.ToString(), (string?)result["row"]!["parentId"]);
        Assert.Equal(hall.Id, (await Row<Location>(shelf.Id)).ParentId);
    }

    // ---- Rows ----

    [Fact]
    public async Task Movement_author_comes_from_the_token()
    {
        var (article, location) = (NewArticle(), NewLocation());
        await _factory.SeedAsync(article, location);
        var movement = NewMovement(article, location, 1000);
        var row = Wire(movement);
        row["createdBy"] = Guid.NewGuid().ToString();
        row["deviceId"] = Guid.NewGuid().ToString();
        var user = await _factory.SignInAsync(Role.Member);

        var answer = await _factory.PushClient(user).PushAsync(("stock_movement", row));

        Assert.Equal("accepted", (string?)answer.Result("stock_movement", movement.Id)["status"]);
        var saved = await Row<StockMovement>(movement.Id);
        Assert.Equal(user.UserId, saved.CreatedBy);
        Assert.Equal(user.SessionId, saved.DeviceId);
    }

    [Fact]
    public async Task Existing_movement_is_accepted_when_identical_and_kept_otherwise()
    {
        var (article, location) = (NewArticle(), NewLocation());
        var same = NewMovement(article, location, 2000);
        var other = NewMovement(article, location, 2000);
        await _factory.SeedAsync(article, location, same, other);
        var changed = Wire(other);
        changed["quantity"] = 3000;

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(("stock_movement", Wire(same)), ("stock_movement", changed));

        Assert.Equal("accepted", (string?)answer.Result("stock_movement", same.Id)["status"]);
        var kept = answer.Result("stock_movement", other.Id);
        Assert.Equal("changed", (string?)kept["status"]);
        Assert.Equal("MOVEMENT_KEPT", (string?)kept["conflict"]);
        Assert.Equal(2000, (long)kept["row"]!["quantity"]!);
        Assert.Equal(other.Id.ToString(), (string?)kept["row"]!["id"]);
        Assert.Equal(2000, (await Row<StockMovement>(other.Id)).Quantity);
    }

    [Fact]
    public async Task Missing_reference_and_invalid_values_are_rejected()
    {
        var (article, location) = (NewArticle(), NewLocation());
        await _factory.SeedAsync(article, location);
        var orphan = Wire(NewMovement(article, location, 1000));
        orphan["articleId"] = Guid.NewGuid().ToString();
        var badReason = Wire(NewMovement(article, location, 1000));
        badReason["reason"] = "stolen";
        var fraction = Wire(NewMovement(article, location, 1000));
        fraction["quantity"] = 1.5;

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(
            ("stock_movement", orphan), ("stock_movement", badReason), ("stock_movement", fraction), ("no_such_table", new JsonObject { ["id"] = "x" }));

        Assert.Equal("missing-reference", (string?)answer.Result("stock_movement", (string)orphan["id"]!)["reason"]);
        Assert.Equal("invalid", (string?)answer.Result("stock_movement", (string)badReason["id"]!)["reason"]);
        Assert.Equal("invalid", (string?)answer.Result("stock_movement", (string)fraction["id"]!)["reason"]);
        Assert.Equal("invalid", (string?)answer.Result("no_such_table", "x")["reason"]);
        Assert.Equal(0, await _factory.QueryAsync(db => db.StockMovements.CountAsync(m => m.ArticleId == article.Id, Ct)));
    }

    [Fact]
    public async Task Second_open_review_of_a_location_is_rejected()
    {
        var location = NewLocation();
        var open = new StockReview { Id = Guid.NewGuid(), LocationId = location.Id, StartedAt = 1, CreatedAt = 1, UpdatedAt = 1 };
        await _factory.SeedAsync(location, open);
        var second = new StockReview { Id = Guid.NewGuid(), LocationId = location.Id, StartedAt = 2, CreatedAt = 2, UpdatedAt = 2 };

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(("stock_review", Wire(second)));

        var result = answer.Result("stock_review", second.Id);
        Assert.Equal("rejected", (string?)result["status"]);
        Assert.Equal("review-open", (string?)result["reason"]);
    }

    // ---- First change wins ----

    private Task<long> SeqOf<T>(Guid id) where T : class =>
        _factory.QueryAsync(db => db.Set<T>().Where(e => EF.Property<Guid>(e, "Id") == id)
            .Select(e => EF.Property<long>(e, nameof(Category.ServerSeq))).SingleAsync(Ct));

    [Fact]
    public async Task Stale_edit_is_rejected_with_the_server_row_and_wins_when_pushed_again_from_it()
    {
        // Spec "Push processing": two phones edit the same version; the first push wins, the second gets a conflict.
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var phoneA = _factory.PushClient(await _factory.SignInAsync(Role.Member));
        var phoneB = _factory.PushClient(await _factory.SignInAsync(Role.Member));
        var first = Edit(article); first["name"] = Unique("first");
        var second = Edit(article); second["name"] = Unique("second");
        second["updatedAt"] = 99; // a newer updatedAt changes nothing

        var a = (await phoneA.PushAsync(("article", first))).Result("article", article.Id);
        Assert.Equal("accepted", (string?)a["status"]);
        Assert.Equal(await SeqOf<Article>(article.Id), (long)a["serverSeq"]!);
        Assert.True((long)a["serverSeq"]! > article.ServerSeq);

        var b = (await phoneB.PushAsync(("article", second))).Result("article", article.Id);
        Assert.Equal("rejected", (string?)b["status"]);
        Assert.Equal("conflict", (string?)b["reason"]);
        Assert.Equal((string?)first["name"], (string?)b["row"]!["name"]); // the server row, for the popup
        Assert.Equal((long)a["serverSeq"]!, (long)b["serverSeq"]!);
        Assert.Equal((string?)first["name"], (await Row<Article>(article.Id)).Name);

        // "Keep mine": the same change again, based on the server version just received.
        second["baseSeq"] = (long)b["serverSeq"]!;
        var again = (await phoneB.PushAsync(("article", second))).Result("article", article.Id);
        Assert.Equal("accepted", (string?)again["status"]);
        Assert.Equal(await SeqOf<Article>(article.Id), (long)again["serverSeq"]!);
        Assert.Equal((string?)second["name"], (await Row<Article>(article.Id)).Name);
    }

    [Fact]
    public async Task Same_device_edits_twice_with_the_returned_seq()
    {
        var category = NewCategory();
        await _factory.SeedAsync(category);
        var client = _factory.PushClient(await _factory.SignInAsync(Role.Admin));
        var row = Edit(category);
        row["name"] = Unique("one");

        var first = (await client.PushAsync(("category", row))).Result("category", category.Id);
        row["name"] = Unique("two");
        row["baseSeq"] = (long)first["serverSeq"]!;
        var second = (await client.PushAsync(("category", row))).Result("category", category.Id);

        Assert.Equal("accepted", (string?)first["status"]);
        Assert.Equal("accepted", (string?)second["status"]);
        Assert.Equal((string?)row["name"], (await Row<Category>(category.Id)).Name);
    }

    [Fact]
    public async Task Delete_and_edit_of_the_same_version_conflict_both_ways()
    {
        var (deletedFirst, editedFirst) = (NewArticle(), NewArticle());
        await _factory.SeedAsync(deletedFirst, editedFirst);
        var phoneA = _factory.PushClient(await _factory.SignInAsync(Role.Admin));
        var phoneB = _factory.PushClient(await _factory.SignInAsync(Role.Admin));

        // Deleted, then edited from the old version: the delete stays, the edit gets the deleted row.
        var delete = Edit(deletedFirst); delete["deletedAt"] = 50;
        var edit = Edit(deletedFirst); edit["name"] = Unique("edited");
        Assert.Equal("accepted", (string?)(await phoneA.PushAsync(("article", delete))).Result("article", deletedFirst.Id)["status"]);
        var late = (await phoneB.PushAsync(("article", edit))).Result("article", deletedFirst.Id);
        Assert.Equal("conflict", (string?)late["reason"]);
        Assert.Equal(50, (long)late["row"]!["deletedAt"]!);
        Assert.Equal(50, (await Row<Article>(deletedFirst.Id)).DeletedAt);

        // Edited, then deleted from the old version: the edit stays, the delete gets the edited row.
        edit = Edit(editedFirst); edit["name"] = Unique("edited");
        delete = Edit(editedFirst); delete["deletedAt"] = 50;
        Assert.Equal("accepted", (string?)(await phoneA.PushAsync(("article", edit))).Result("article", editedFirst.Id)["status"]);
        late = (await phoneB.PushAsync(("article", delete))).Result("article", editedFirst.Id);
        Assert.Equal("conflict", (string?)late["reason"]);
        Assert.Equal((string?)edit["name"], (string?)late["row"]!["name"]);
        var saved = await Row<Article>(editedFirst.Id);
        Assert.Null(saved.DeletedAt);
        Assert.Equal((string?)edit["name"], saved.Name);
    }

    [Fact]
    public async Task Edit_without_base_seq_conflicts_new_rows_do_not()
    {
        var (article, category) = (NewArticle(), NewCategory());
        await _factory.SeedAsync(article, category);
        var member = _factory.PushClient(await _factory.SignInAsync(Role.Member));
        var unseen = Wire(article); // the phone never received this row
        unseen["name"] = Unique("unseen");
        var fresh = NewArticle();
        var freshWithBase = Wire(NewArticle());
        freshWithBase["baseSeq"] = 1; // ignored on an insert
        var badBase = Edit(article);
        badBase["baseSeq"] = "7";
        var notAllowed = Wire(category); // a Member may not edit categories: that comes before the conflict
        notAllowed["name"] = Unique("renamed");

        var answer = await member.PushAsync(("article", unseen), ("article", Wire(fresh)), ("article", freshWithBase), ("category", notAllowed));
        var badAnswer = await member.PushAsync(("article", badBase));

        Assert.Equal("conflict", (string?)answer.Result("article", article.Id)["reason"]);
        Assert.Equal(article.Name, (await Row<Article>(article.Id)).Name);
        var inserted = answer.Result("article", fresh.Id);
        Assert.Equal("accepted", (string?)inserted["status"]);
        Assert.Equal(await SeqOf<Article>(fresh.Id), (long)inserted["serverSeq"]!);
        Assert.Equal("accepted", (string?)answer.Result("article", (string)freshWithBase["id"]!)["status"]);
        Assert.Equal("not-allowed", (string?)answer.Result("category", category.Id)["reason"]);
        Assert.Equal("invalid", (string?)badAnswer.Result("article", article.Id)["reason"]);
    }

    [Fact]
    public async Task Replayed_batch_with_a_conflict_returns_the_first_answer()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var user = await _factory.SignInAsync(Role.Member);
        var client = _factory.PushClient(user);
        var winner = Edit(article); winner["name"] = Unique("winner");
        await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(("article", winner));
        var stale = Edit(article); stale["name"] = Unique("stale");
        var body = Body(Guid.NewGuid(), ("article", stale));

        var first = await client.PushAsync(body);
        var again = await client.PushAsync(body);

        Assert.Equal("conflict", (string?)first.Result("article", article.Id)["reason"]);
        Assert.Equal(first.Text, again.Text);
        Assert.Equal((string?)winner["name"], (await Row<Article>(article.Id)).Name);
    }

    [Fact]
    public async Task Identical_row_is_accepted_without_a_new_seq()
    {
        var category = NewCategory();
        await _factory.SeedAsync(category);
        var seq = (await Row<Category>(category.Id)).ServerSeq;

        // Even for a Member, who may not edit categories: nothing changes.
        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(("category", Wire(category)));

        Assert.Equal("accepted", (string?)answer.Result("category", category.Id)["status"]);
        Assert.Equal(seq, (long)answer.Result("category", category.Id)["serverSeq"]!);
        Assert.Equal(seq, (await Row<Category>(category.Id)).ServerSeq);
    }

    [Fact]
    public async Task Changed_answer_has_only_the_changed_fields()
    {
        var taken = NewArticle();
        taken.Sku = Unique("SKU");
        taken.Barcode = Unique("TAG");
        await _factory.SeedAsync(taken);
        var incoming = NewArticle();
        incoming.Sku = taken.Sku.ToLowerInvariant();
        incoming.Barcode = taken.Barcode;

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(("article", Wire(incoming)));

        var result = answer.Result("article", incoming.Id);
        Assert.Equal("changed", (string?)result["status"]);
        Assert.Equal("SKU_RENAMED", (string?)result["conflict"]); // the first rule that applied
        Assert.Equal(await SeqOf<Article>(incoming.Id), (long)result["serverSeq"]!);
        var row = result["row"]!.AsObject();
        Assert.Equal(["barcode", "sku"], row.Select(f => f.Key).Order());
        Assert.Equal(incoming.Sku + " (2)", (string?)row["sku"]);
        Assert.Null(row["barcode"]);
        Assert.True(row.ContainsKey("barcode"));
    }

    [Fact]
    public async Task Merged_attribute_redirects_links_of_the_same_batch()
    {
        var category = NewCategory();
        var server = NewAttribute();
        server.DataType = "measure";
        server.Unit = "mm";
        await _factory.SeedAsync(category, server);
        var phone = NewAttribute();
        phone.Key = server.Key;
        phone.Label = "Length (phone)";
        var link = new CategoryAttribute { Id = Guid.NewGuid(), CategoryId = category.Id, AttributeId = phone.Id, CreatedAt = 1, UpdatedAt = 1 };

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Admin)).PushAsync(
            ("attribute_definition", Wire(phone)), ("category_attribute", Wire(link)));

        var merged = answer.Result("attribute_definition", phone.Id);
        Assert.Equal("ATTRIBUTE_MERGED", (string?)merged["conflict"]);
        Assert.Equal(server.Id.ToString(), (string?)merged["row"]!["id"]);
        Assert.Equal(await SeqOf<AttributeDefinition>(server.Id), (long)merged["serverSeq"]!);
        Assert.Equal("measure", (string?)merged["row"]!["dataType"]); // number -> measure is one way
        Assert.Equal("accepted", (string?)answer.Result("category_attribute", link.Id)["status"]);
        Assert.Equal(server.Id, (await Row<CategoryAttribute>(link.Id)).AttributeId);
        Assert.Equal("Length (phone)", (await Row<AttributeDefinition>(server.Id)).Label);
        Assert.False(await _factory.QueryAsync(db => db.AttributeDefinitions.AnyAsync(d => d.Id == phone.Id, Ct)));
    }

    [Fact]
    public async Task Attribute_key_never_changes()
    {
        var attribute = NewAttribute();
        await _factory.SeedAsync(attribute);
        var row = Edit(attribute);
        row["key"] = Unique("other");
        row["label"] = "New label";

        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Admin)).PushAsync(("attribute_definition", row));

        var result = answer.Result("attribute_definition", attribute.Id);
        Assert.Equal("ATTRIBUTE_KEY_KEPT", (string?)result["conflict"]);
        Assert.Equal(attribute.Key, (string?)result["row"]!["key"]);
        var saved = await Row<AttributeDefinition>(attribute.Id);
        Assert.Equal(attribute.Key, saved.Key);
        Assert.Equal("New label", saved.Label);
    }

    // ---- Low-stock alerts ----

    private async Task<(Article Article, Location Location)> StockedAsync(long threshold, long stock, string tracking = "quantity", bool archived = false)
    {
        var article = NewArticle(threshold, tracking);
        article.Archived = archived;
        var location = NewLocation();
        await _factory.SeedAsync(article, location, NewMovement(article, location, stock));
        return (article, location);
    }

    private Task<List<LowStockAlert>> AlertsAsync(Guid article) =>
        _factory.QueryAsync(db => db.Set<LowStockAlert>().Where(a => a.ArticleId == article).ToListAsync(Ct));

    [Theory]
    [InlineData(-1000, true)]  // 5000: exactly at the threshold
    [InlineData(-1001, true)]  // just under
    [InlineData(-999, false)]  // just above
    public async Task Crossing_the_threshold_alerts_once(long quantity, bool alerts)
    {
        var (article, location) = await StockedAsync(threshold: 5000, stock: 6000);
        var user = await _factory.SignInAsync(Role.Member);

        await _factory.PushClient(user).PushAsync(("stock_movement", Wire(NewMovement(article, location, quantity, "consumption"))));

        var list = await AlertsAsync(article.Id);
        if (!alerts) { Assert.Empty(list); return; }
        var alert = Assert.Single(list);
        Assert.Equal(6000 + quantity, alert.Stock);
        Assert.Equal(user.UserId, alert.CausedByUserId);
        Assert.Equal(_factory.Time.GetUtcNow().ToUnixTimeMilliseconds(), alert.CrossedAt); // the server clock is frozen
        Assert.True(alert.ServerSeq > 0);
    }

    [Fact]
    public async Task Under_and_back_within_one_batch_does_not_alert()
    {
        var (article, location) = await StockedAsync(threshold: 5000, stock: 6000);
        await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(
            ("stock_movement", Wire(NewMovement(article, location, -3000, "consumption"))),
            ("stock_movement", Wire(NewMovement(article, location, 3000))));

        Assert.Empty(await AlertsAsync(article.Id));
    }

    [Fact]
    public async Task Stays_low_without_a_new_alert_and_alerts_again_after_going_back_up()
    {
        var (article, location) = await StockedAsync(threshold: 5000, stock: 6000);
        var client = _factory.PushClient(await _factory.SignInAsync(Role.Member));

        await client.PushAsync(("stock_movement", Wire(NewMovement(article, location, -2000, "consumption"))));
        await client.PushAsync(("stock_movement", Wire(NewMovement(article, location, -1000, "consumption"))));
        Assert.Single(await AlertsAsync(article.Id));

        await client.PushAsync(("stock_movement", Wire(NewMovement(article, location, 5000))));
        await client.PushAsync(("stock_movement", Wire(NewMovement(article, location, -5000, "consumption"))));
        Assert.Equal(2, (await AlertsAsync(article.Id)).Count);
    }

    [Theory]
    [InlineData("serial", false)]
    [InlineData("quantity", true)]
    public async Task Serial_and_archived_articles_never_alert(string tracking, bool archived)
    {
        var (article, location) = await StockedAsync(threshold: 5000, stock: 6000, tracking, archived);
        await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(
            ("stock_movement", Wire(NewMovement(article, location, -3000, "consumption"))));

        Assert.Empty(await AlertsAsync(article.Id));
    }

    [Fact]
    public async Task Setting_a_threshold_on_a_low_article_alerts()
    {
        var article = NewArticle();
        var location = NewLocation();
        await _factory.SeedAsync(article, location, NewMovement(article, location, 1000));
        var row = Edit(article);
        row["threshold"] = 2000;

        await _factory.PushClient(await _factory.SignInAsync(Role.Member)).PushAsync(("article", row));

        Assert.Equal(1000, Assert.Single(await AlertsAsync(article.Id)).Stock);
    }

    // ---- Foreign keys ----

    [Fact]
    public async Task Guest_logout_still_deletes_the_guest_with_its_sync_batches()
    {
        var guest = await _factory.SignInAsync(Role.Guest);
        var batchId = Guid.NewGuid();
        await _factory.WithDb(async db =>
        {
            db.Set<SyncBatch>().Add(new SyncBatch
            {
                BatchId = batchId, UserId = guest.UserId, DeviceSessionId = guest.SessionId,
                ReceivedAt = _factory.Time.GetUtcNow(), Response = "{}",
            });
            await db.SaveChangesAsync(Ct);
        });

        var logout = await _factory.ClientFor(guest).PostAsync("/api/v1/auth/logout", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await _factory.WithDb(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == guest.UserId, Ct));
            Assert.False(await db.Set<SyncBatch>().AnyAsync(b => b.BatchId == batchId, Ct));
        });
    }
}

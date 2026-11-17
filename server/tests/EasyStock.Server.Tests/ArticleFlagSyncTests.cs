using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EasyStock.Server.Tests.Infrastructure.SyncPushKit;

namespace EasyStock.Server.Tests;

/// <summary>App data schema 13: article.order_url, the attribute flag columns and the article_flag table.</summary>
public sealed class ArticleFlagSyncTests(PostgresFixture pg) : IDisposable
{
    private const string FlagTable = "article_flag";
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private static Article NewArticle() => new() { Id = Guid.NewGuid(), Name = Unique("art"), CreatedAt = 1, UpdatedAt = 10 };

    private static ArticleFlag NewFlag(Article a, string note = "Label is torn") =>
        new() { Id = Guid.NewGuid(), ArticleId = a.Id, Note = note, CreatedAt = 5, UpdatedAt = 5 };

    private Task<T> Row<T>(Guid id) where T : class =>
        _factory.QueryAsync(db => db.Set<T>().AsNoTracking().SingleAsync(e => EF.Property<Guid>(e, "Id") == id, Ct));

    private async Task<long> CursorAsync()
    {
        _factory.CreateClient(); // starts the host, which migrates the database
        return await _factory.QueryAsync(db => db.Database.SqlQuery<long>($"SELECT last_value AS \"Value\" FROM server_seq").SingleAsync(Ct));
    }

    private async Task<List<JsonElement>> PullRowsAsync(SignedInUser user, long since, string table)
    {
        var rows = new List<JsonElement>();
        for (var cursor = since; ;)
        {
            var response = await _factory.ClientFor(user).GetAsync($"/api/v1/sync/pull?since={cursor}&limit=2000", Ct);
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.GetProperty("rows").TryGetProperty(table, out var found))
                rows.AddRange(found.EnumerateArray().Select(r => r.Clone()));
            if (!doc.RootElement.GetProperty("hasMore").GetBoolean()) return rows;
            cursor = doc.RootElement.GetProperty("next").GetInt64();
        }
    }

    // ---- article.order_url and the attribute flags ----

    [Fact]
    public async Task Order_url_and_the_two_attribute_flags_round_trip_through_push_and_pull()
    {
        var since = await CursorAsync();
        var member = await _factory.SignInAsync(Role.Member);
        var article = NewArticle();
        article.OrderUrl = "https://shop.example/order?sku=1";
        var attribute = new AttributeDefinition
        {
            Id = Guid.NewGuid(), Key = Unique("expiry"), Label = "Expiry", DataType = "date",
            FlagWhenPast = true, NotifyWhenPast = true, CreatedAt = 1, UpdatedAt = 10,
        };

        var wireArticle = Wire(article);
        var wireAttribute = Wire(attribute);
        Assert.Equal("https://shop.example/order?sku=1", (string?)wireArticle["orderUrl"]);
        Assert.True((bool)wireAttribute["flagWhenPast"]!);
        Assert.True((bool)wireAttribute["notifyWhenPast"]!);

        var answer = await _factory.PushClient(member).PushAsync(("attribute_definition", wireAttribute), ("article", wireArticle));
        Assert.Equal("accepted", (string?)answer.Result("article", article.Id)["status"]);
        Assert.Equal("accepted", (string?)answer.Result("attribute_definition", attribute.Id)["status"]);

        Assert.Equal("https://shop.example/order?sku=1", (await Row<Article>(article.Id)).OrderUrl);
        var savedAttribute = await Row<AttributeDefinition>(attribute.Id);
        Assert.True(savedAttribute.FlagWhenPast);
        Assert.True(savedAttribute.NotifyWhenPast);

        var viewer = await _factory.SignInAsync(Role.Viewer);
        var articles = await PullRowsAsync(viewer, since, "article");
        Assert.Equal("https://shop.example/order?sku=1", articles.Single(r => r.GetProperty("id").GetString() == article.Id.ToString()).GetProperty("orderUrl").GetString());
        var attributes = await PullRowsAsync(viewer, since, "attribute_definition");
        var pulled = attributes.Single(r => r.GetProperty("id").GetString() == attribute.Id.ToString());
        Assert.True(pulled.GetProperty("flagWhenPast").GetBoolean());
        Assert.True(pulled.GetProperty("notifyWhenPast").GetBoolean());
    }

    [Fact]
    public async Task A_row_without_the_new_fields_takes_the_defaults_and_they_can_be_cleared()
    {
        var member = await _factory.SignInAsync(Role.Member);
        var article = NewArticle();
        var attribute = new AttributeDefinition { Id = Guid.NewGuid(), Key = Unique("key"), Label = "Date", DataType = "date", CreatedAt = 1, UpdatedAt = 10 };
        var wireArticle = Wire(article); wireArticle.Remove("orderUrl");
        var wireAttribute = Wire(attribute); wireAttribute.Remove("flagWhenPast"); wireAttribute.Remove("notifyWhenPast");
        await _factory.PushClient(member).PushAsync(("attribute_definition", wireAttribute), ("article", wireArticle));

        Assert.Null((await Row<Article>(article.Id)).OrderUrl);
        var saved = await Row<AttributeDefinition>(attribute.Id);
        Assert.False(saved.FlagWhenPast);
        Assert.False(saved.NotifyWhenPast);

        var setUrl = Edit(await Row<Article>(article.Id)); setUrl["orderUrl"] = "https://shop.example/a"; setUrl["updatedAt"] = 20;
        var setFlags = Edit(saved); setFlags["flagWhenPast"] = true; setFlags["updatedAt"] = 20;
        await _factory.PushClient(member).PushAsync(("attribute_definition", setFlags), ("article", setUrl));
        Assert.Equal("https://shop.example/a", (await Row<Article>(article.Id)).OrderUrl);
        Assert.True((await Row<AttributeDefinition>(attribute.Id)).FlagWhenPast);

        var clearUrl = Edit(await Row<Article>(article.Id)); clearUrl["orderUrl"] = null; clearUrl["updatedAt"] = 30;
        var clearFlags = Edit(await Row<AttributeDefinition>(attribute.Id)); clearFlags["flagWhenPast"] = false; clearFlags["updatedAt"] = 30;
        await _factory.PushClient(member).PushAsync(("attribute_definition", clearFlags), ("article", clearUrl));
        Assert.Null((await Row<Article>(article.Id)).OrderUrl);
        Assert.False((await Row<AttributeDefinition>(attribute.Id)).FlagWhenPast);
    }

    [Fact]
    public async Task An_empty_or_too_long_order_url_is_invalid()
    {
        var member = await _factory.SignInAsync(Role.Member);
        var empty = NewArticle(); var tooLong = NewArticle();
        var wireEmpty = Wire(empty); wireEmpty["orderUrl"] = "";
        var wireLong = Wire(tooLong); wireLong["orderUrl"] = "https://x.example/" + new string('a', 2100);
        var answer = await _factory.PushClient(member).PushAsync(("article", wireEmpty), ("article", wireLong));
        Assert.Equal("invalid", (string?)answer.Result("article", empty.Id)["reason"]);
        Assert.Equal("invalid", (string?)answer.Result("article", tooLong.Id)["reason"]);
    }

    // ---- article_flag push ----

    [Theory]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Member)]
    [InlineData(Role.Admin)]
    public async Task A_flag_is_created_with_the_author_of_the_token_whatever_the_phone_sends(Role role)
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var user = await _factory.SignInAsync(role);
        var flag = NewFlag(article);
        var row = Wire(flag);
        row["createdBy"] = Guid.NewGuid(); // never read

        var answer = await _factory.PushClient(user).PushAsync((FlagTable, row));

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        var result = answer.Result(FlagTable, flag.Id);
        Assert.Equal("accepted", (string?)result["status"]);
        Assert.Equal(user.UserId.ToString(), (string?)result["row"]!["createdBy"]);
        var saved = await Row<ArticleFlag>(flag.Id);
        Assert.Equal(user.UserId, saved.CreatedBy);
        Assert.Equal(article.Id, saved.ArticleId);
        Assert.Equal("Label is torn", saved.Note);
        Assert.Null(saved.ResolvedAt);
        Assert.True(saved.ServerSeq > 0);
    }

    [Fact]
    public async Task A_flag_comes_after_its_article_in_the_same_batch()
    {
        var viewer = await _factory.SignInAsync(Role.Viewer);
        var member = await _factory.SignInAsync(Role.Member);
        var article = NewArticle();
        var flag = NewFlag(article);
        var answer = await _factory.PushClient(member).PushAsync((FlagTable, Wire(flag)), ("article", Wire(article)));
        Assert.Equal("accepted", (string?)answer.Result(FlagTable, flag.Id)["status"]);

        // A Viewer's phone pushes the flag only: its article is on the server already.
        var second = NewFlag(article, "Out of stock sign");
        Assert.Equal("accepted", (string?)(await _factory.PushClient(viewer).PushAsync((FlagTable, Wire(second)))).Result(FlagTable, second.Id)["status"]);
    }

    [Fact]
    public async Task A_member_resolves_edits_and_deletes_a_flag_and_the_author_stays()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var viewer = await _factory.SignInAsync(Role.Viewer);
        var member = await _factory.SignInAsync(Role.Member);
        var flag = NewFlag(article);
        await _factory.PushClient(viewer).PushAsync((FlagTable, Wire(flag)));

        var resolve = Edit(await Row<ArticleFlag>(flag.Id));
        resolve["resolvedAt"] = 50; resolve["updatedAt"] = 50; resolve["createdBy"] = Guid.NewGuid();
        var answer = await _factory.PushClient(member).PushAsync((FlagTable, resolve));
        Assert.Equal("accepted", (string?)answer.Result(FlagTable, flag.Id)["status"]);
        var resolved = await Row<ArticleFlag>(flag.Id);
        Assert.Equal(50, resolved.ResolvedAt);
        Assert.Equal(viewer.UserId, resolved.CreatedBy);

        var edit = Edit(resolved); edit["note"] = "Label replaced"; edit["updatedAt"] = 60;
        await _factory.PushClient(member).PushAsync((FlagTable, edit));
        var edited = await Row<ArticleFlag>(flag.Id);
        Assert.Equal("Label replaced", edited.Note);
        Assert.Equal(viewer.UserId, edited.CreatedBy);

        var delete = Edit(edited); delete["deletedAt"] = 70; delete["updatedAt"] = 70;
        await _factory.PushClient(member).PushAsync((FlagTable, delete));
        var deleted = await Row<ArticleFlag>(flag.Id);
        Assert.Equal(70, deleted.DeletedAt);
        Assert.Equal(viewer.UserId, deleted.CreatedBy);
    }

    [Fact]
    public async Task An_admin_can_resolve_a_flag()
    {
        var article = NewArticle();
        var flag = NewFlag(article);
        await _factory.SeedAsync(article, flag);
        var resolve = Edit(flag); resolve["resolvedAt"] = 50; resolve["updatedAt"] = 50;
        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Admin)).PushAsync((FlagTable, resolve));
        Assert.Equal("accepted", (string?)answer.Result(FlagTable, flag.Id)["status"]);
        Assert.Equal(50, (await Row<ArticleFlag>(flag.Id)).ResolvedAt);
    }

    [Fact]
    public async Task A_viewer_can_create_a_flag_but_not_resolve_edit_or_delete_it()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var viewer = await _factory.SignInAsync(Role.Viewer);
        var flag = NewFlag(article);
        await _factory.PushClient(viewer).PushAsync((FlagTable, Wire(flag)));
        var saved = await Row<ArticleFlag>(flag.Id);

        var resolve = Edit(saved); resolve["resolvedAt"] = 50; resolve["updatedAt"] = 50;
        var edit = Edit(saved); edit["note"] = "changed"; edit["updatedAt"] = 50;
        var delete = Edit(saved); delete["deletedAt"] = 50; delete["updatedAt"] = 50;
        foreach (var row in new[] { resolve, edit, delete })
        {
            var answer = await _factory.PushClient(viewer).PushAsync((FlagTable, row));
            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal("rejected", (string?)answer.Result(FlagTable, flag.Id)["status"]);
            Assert.Equal("not-allowed", (string?)answer.Result(FlagTable, flag.Id)["reason"]);
        }
        var unchanged = await Row<ArticleFlag>(flag.Id);
        Assert.Null(unchanged.ResolvedAt);
        Assert.Null(unchanged.DeletedAt);
        Assert.Equal("Label is torn", unchanged.Note);
    }

    [Fact]
    public async Task A_viewer_cannot_create_a_flag_that_is_already_resolved_or_deleted()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var viewer = await _factory.SignInAsync(Role.Viewer);
        var resolved = NewFlag(article); resolved.ResolvedAt = 9;
        var deleted = NewFlag(article); deleted.DeletedAt = 9;
        var answer = await _factory.PushClient(viewer).PushAsync((FlagTable, Wire(resolved)), (FlagTable, Wire(deleted)));
        Assert.Equal("not-allowed", (string?)answer.Result(FlagTable, resolved.Id)["reason"]);
        Assert.Equal("not-allowed", (string?)answer.Result(FlagTable, deleted.Id)["reason"]);
        Assert.False(await _factory.QueryAsync(db => db.Set<ArticleFlag>().AnyAsync(f => f.Id == resolved.Id || f.Id == deleted.Id, Ct)));
    }

    [Fact]
    public async Task A_guest_cannot_push_a_flag()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var flag = NewFlag(article);
        var answer = await _factory.PushClient(await _factory.SignInAsync(Role.Guest)).PushAsync((FlagTable, Wire(flag)));
        Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
        Assert.False(await _factory.QueryAsync(db => db.Set<ArticleFlag>().AnyAsync(f => f.Id == flag.Id, Ct)));
    }

    [Fact]
    public async Task A_flag_needs_an_existing_article_a_note_and_its_required_fields()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var member = await _factory.SignInAsync(Role.Member);
        var orphan = NewFlag(NewArticle());
        var blank = NewFlag(article, "   ");
        var noNote = Wire(NewFlag(article)); var noNoteId = (string)noNote["id"]!; noNote.Remove("note");
        var noArticle = Wire(NewFlag(article)); var noArticleId = (string)noArticle["id"]!; noArticle.Remove("articleId");
        var negative = NewFlag(article); negative.ResolvedAt = -1;

        var answer = await _factory.PushClient(member).PushAsync(
            (FlagTable, Wire(orphan)), (FlagTable, Wire(blank)), (FlagTable, noNote), (FlagTable, noArticle), (FlagTable, Wire(negative)));

        Assert.Equal("missing-reference", (string?)answer.Result(FlagTable, orphan.Id)["reason"]);
        Assert.Equal("invalid", (string?)answer.Result(FlagTable, blank.Id)["reason"]);
        Assert.Equal("invalid", (string?)answer.Result(FlagTable, noNoteId)["reason"]);
        Assert.Equal("invalid", (string?)answer.Result(FlagTable, noArticleId)["reason"]);
        Assert.Equal("invalid", (string?)answer.Result(FlagTable, negative.Id)["reason"]);
    }

    [Fact]
    public async Task A_stale_flag_edit_is_a_conflict_and_a_replayed_creation_changes_nothing()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var viewer = await _factory.SignInAsync(Role.Viewer);
        var member = await _factory.SignInAsync(Role.Member);
        var flag = NewFlag(article);
        var batch = Body(Guid.NewGuid(), (FlagTable, Wire(flag)));
        var first = await _factory.PushClient(viewer).PushAsync(batch);
        Assert.Equal(first.Text, (await _factory.PushClient(viewer).PushAsync(batch)).Text);

        var stale = Edit(await Row<ArticleFlag>(flag.Id)); stale["baseSeq"] = 1; stale["resolvedAt"] = 50; stale["updatedAt"] = 50;
        var answer = await _factory.PushClient(member).PushAsync((FlagTable, stale));
        Assert.Equal("conflict", (string?)answer.Result(FlagTable, flag.Id)["reason"]);
        Assert.Null((await Row<ArticleFlag>(flag.Id)).ResolvedAt);
    }

    [Fact]
    public async Task A_flag_note_is_at_most_1000_characters()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var member = await _factory.SignInAsync(Role.Member);
        var ok = NewFlag(article, new string('n', 1000));
        var tooLong = NewFlag(article, new string('n', 1001));
        var answer = await _factory.PushClient(member).PushAsync((FlagTable, Wire(ok)), (FlagTable, Wire(tooLong)));
        Assert.Equal("accepted", (string?)answer.Result(FlagTable, ok.Id)["status"]);
        Assert.Equal("invalid", (string?)answer.Result(FlagTable, tooLong.Id)["reason"]);
    }

    [Fact]
    public async Task A_flag_cannot_be_resolved_before_it_was_created()
    {
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var member = await _factory.SignInAsync(Role.Member);
        var early = NewFlag(article); early.CreatedAt = 100; early.ResolvedAt = 99; early.UpdatedAt = 100;
        var same = NewFlag(article); same.CreatedAt = 100; same.ResolvedAt = 100; same.UpdatedAt = 100;
        var answer = await _factory.PushClient(member).PushAsync((FlagTable, Wire(early)), (FlagTable, Wire(same)));
        Assert.Equal("invalid", (string?)answer.Result(FlagTable, early.Id)["reason"]);
        Assert.Equal("accepted", (string?)answer.Result(FlagTable, same.Id)["status"]);
    }

    [Fact]
    public async Task A_new_flag_on_a_deleted_article_is_refused_but_an_existing_one_can_still_be_resolved()
    {
        var live = NewArticle();
        var flag = NewFlag(live);
        await _factory.SeedAsync(live, flag);
        var dead = NewArticle(); dead.DeletedAt = 9;
        await _factory.SeedAsync(dead);
        var viewer = await _factory.SignInAsync(Role.Viewer);
        var member = await _factory.SignInAsync(Role.Member);

        var fresh = NewFlag(dead);
        var answer = await _factory.PushClient(viewer).PushAsync((FlagTable, Wire(fresh)));
        Assert.Equal("missing-reference", (string?)answer.Result(FlagTable, fresh.Id)["reason"]);
        Assert.False(await _factory.QueryAsync(db => db.Set<ArticleFlag>().AnyAsync(f => f.Id == fresh.Id, Ct)));

        // The article is deleted after the flag was raised: resolving it still works.
        await _factory.QueryAsync(db => db.Set<Article>().Where(a => a.Id == live.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.DeletedAt, 9L), Ct));
        var resolve = Edit(flag); resolve["resolvedAt"] = 50; resolve["updatedAt"] = 50;
        var resolved = await _factory.PushClient(member).PushAsync((FlagTable, resolve));
        Assert.Equal("accepted", (string?)resolved.Result(FlagTable, flag.Id)["status"]);
    }

    // ---- article_flag pull ----

    [Fact]
    public async Task Pull_sends_flags_with_their_author_to_everyone_but_a_guest()
    {
        var since = await CursorAsync();
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var viewer = await _factory.SignInAsync(Role.Viewer);
        var flag = NewFlag(article);
        await _factory.PushClient(viewer).PushAsync((FlagTable, Wire(flag)));

        foreach (var role in new[] { Role.Viewer, Role.Member, Role.Admin })
        {
            var rows = await PullRowsAsync(await _factory.SignInAsync(role), since, FlagTable);
            var row = rows.Single(r => r.GetProperty("id").GetString() == flag.Id.ToString());
            Assert.Equal(viewer.UserId.ToString(), row.GetProperty("createdBy").GetString());
            Assert.Equal(article.Id.ToString(), row.GetProperty("articleId").GetString());
            Assert.Equal("Label is torn", row.GetProperty("note").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("resolvedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("deletedAt").ValueKind);
            Assert.True(row.GetProperty("serverSeq").GetInt64() > since);
            Assert.False(row.TryGetProperty("deviceId", out _));
        }
        Assert.Empty(await PullRowsAsync(await _factory.SignInAsync(Role.Guest), since, FlagTable));
    }

    [Fact]
    public async Task Pull_names_the_author_of_a_flag_even_without_a_movement()
    {
        var since = await CursorAsync();
        var article = NewArticle();
        await _factory.SeedAsync(article);
        var viewer = await _factory.SignInAsync(Role.Viewer);
        await _factory.PushClient(viewer).PushAsync((FlagTable, Wire(NewFlag(article))));

        var response = await _factory.ClientFor(await _factory.SignInAsync(Role.Member)).GetAsync($"/api/v1/sync/pull?since={since}&limit=2000", Ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Contains(doc.RootElement.GetProperty("users").EnumerateArray(), u => u.GetProperty("id").GetString() == viewer.UserId.ToString());
    }
}

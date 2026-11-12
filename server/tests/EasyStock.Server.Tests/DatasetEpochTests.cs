using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Sync;
using EasyStock.Server.Tests.Infrastructure;
using EasyStock.Server.DB;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EasyStock.Server.Tests.Infrastructure.SyncPushKit;

namespace EasyStock.Server.Tests;

/// <summary>
/// The dataset epoch: in server-info and every pull, checked from X-EasyStock-Dataset-Epoch on pull and push, and
/// changed by a replace import. Each test has a database of its own, since a replace changes the epoch for everyone.
/// </summary>
public sealed class DatasetEpochTests(PostgresFixture pg) : IAsyncLifetime
{
    private const string Header = "X-EasyStock-Dataset-Epoch";
    private readonly List<ApiFactory> _factories = [];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var f in _factories) f.Dispose();
        return ValueTask.CompletedTask;
    }

    private ApiFactory Start(string connectionString)
    {
        var factory = new ApiFactory(connectionString);
        _factories.Add(factory);
        return factory;
    }

    private async Task<ApiFactory> FreshAsync() => Start(await pg.CreateDatabaseAsync());

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string> ServerInfoEpoch(ApiFactory f) =>
        (await Json(await f.CreateClient().GetAsync("/api/v1/server-info", Ct))).GetProperty("datasetEpoch").GetString()!;

    private static HttpClient WithEpoch(HttpClient client, string? epoch)
    {
        if (epoch is not null) client.DefaultRequestHeaders.Add(Header, epoch);
        return client;
    }

    private static Task<HttpResponseMessage> Pull(HttpClient client) => client.GetAsync("/api/v1/sync/pull?since=0&limit=10", Ct);

    private static Task<PushAnswer> EmptyPush(HttpClient client) =>
        client.PushAsync(new JsonObject { ["batchId"] = Guid.NewGuid().ToString(), ["rows"] = new JsonObject() });

    private static void AssertReset(HttpStatusCode status, string body, string expectedEpoch)
    {
        Assert.Equal(HttpStatusCode.Conflict, status);
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("dataset-reset", root.GetProperty("type").GetString());
        Assert.Equal(expectedEpoch, root.GetProperty("datasetEpoch").GetString());
    }

    private static async Task Import(HttpClient admin, string mode)
    {
        var export = await admin.GetAsync("/api/v1/admin/backup", Ct);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var content = new ByteArrayContent(await export.Content.ReadAsByteArrayAsync(Ct));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var import = await admin.PostAsync($"/api/v1/admin/backup?mode={mode}", content, Ct);
        Assert.True(import.IsSuccessStatusCode, await import.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Server_info_and_pull_return_the_same_epoch()
    {
        var f = await FreshAsync();
        var epoch = await ServerInfoEpoch(f);
        Assert.True(Guid.TryParse(epoch, out _));

        var pull = await Json(await Pull(f.ClientFor(await f.SignInAsync(Role.Viewer))));
        Assert.Equal(epoch, pull.GetProperty("datasetEpoch").GetString());
    }

    [Fact]
    public async Task A_matching_or_missing_header_is_accepted()
    {
        var f = await FreshAsync();
        var epoch = await ServerInfoEpoch(f);
        var member = await f.SignInAsync(Role.Member);

        Assert.Equal(HttpStatusCode.OK, (await Pull(WithEpoch(f.ClientFor(member), epoch))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Pull(WithEpoch(f.ClientFor(member), epoch.ToUpperInvariant()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Pull(f.ClientFor(member))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await EmptyPush(WithEpoch(f.ClientFor(member), epoch))).Status);
        Assert.Equal(HttpStatusCode.OK, (await EmptyPush(f.ClientFor(member))).Status);
    }

    [Fact]
    public async Task A_different_header_gets_409_dataset_reset_on_pull_and_push()
    {
        var f = await FreshAsync();
        var epoch = await ServerInfoEpoch(f);
        var member = await f.SignInAsync(Role.Member);

        foreach (var stale in new[] { Guid.NewGuid().ToString(), "not-a-uuid" })
        {
            var pull = await Pull(WithEpoch(f.ClientFor(member), stale));
            AssertReset(pull.StatusCode, await pull.Content.ReadAsStringAsync(Ct), epoch);
            var push = await EmptyPush(WithEpoch(f.ClientFor(member), stale));
            AssertReset(push.Status, push.Text, epoch);
        }
    }

    [Fact]
    public async Task Authentication_is_checked_before_the_epoch()
    {
        var f = await FreshAsync();
        var anonymous = WithEpoch(f.CreateVersionedClient(), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pull(anonymous)).StatusCode);
    }

    [Fact]
    public async Task Replace_import_changes_the_epoch_and_the_old_one_gets_409()
    {
        var db = await pg.CreateDatabaseAsync();
        var f = Start(db);
        var before = await ServerInfoEpoch(f);
        await Import(f.ClientFor(await f.SignInAsync(Role.Admin)), "replace");

        var after = await ServerInfoEpoch(f);
        Assert.NotEqual(before, after);
        Assert.True(Guid.TryParse(after, out _));
        var member = await f.SignInAsync(Role.Member);
        var pull = await Pull(WithEpoch(f.ClientFor(member), before));
        AssertReset(pull.StatusCode, await pull.Content.ReadAsStringAsync(Ct), after);
        var push = await EmptyPush(WithEpoch(f.ClientFor(member), before));
        AssertReset(push.Status, push.Text, after);
        Assert.Equal(after, (await Json(await Pull(WithEpoch(f.ClientFor(member), after)))).GetProperty("datasetEpoch").GetString());

        // Stored, not only cached: a restarted server answers the new epoch.
        Assert.Equal(after, await ServerInfoEpoch(Start(db)));
    }

    [Fact]
    public async Task Merge_import_keeps_the_epoch()
    {
        var f = await FreshAsync();
        var before = await ServerInfoEpoch(f);
        await Import(f.ClientFor(await f.SignInAsync(Role.Admin)), "merge");
        Assert.Equal(before, await ServerInfoEpoch(f));
    }

    [Fact]
    public async Task The_epoch_is_stable_across_restarts()
    {
        var db = await pg.CreateDatabaseAsync();
        var first = await ServerInfoEpoch(Start(db));
        var second = await ServerInfoEpoch(Start(db));
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Push_checks_the_stored_epoch_under_the_lock()
    {
        // The cache is only updated after an import commits; a push that waited on that import must still see the
        // new epoch. Simulated by changing the stored value behind the cache's back.
        var f = await FreshAsync();
        var cached = await ServerInfoEpoch(f);
        var stored = await f.WithService<string, DatasetEpochHandler>(async epochs =>
        {
            await using var scope = f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(Ct);
            var value = await epochs.RotateAsync(db, Ct);
            await tx.CommitAsync(Ct);
            return value;
        });
        var member = await f.SignInAsync(Role.Member);

        Assert.Equal(HttpStatusCode.OK, (await Pull(WithEpoch(f.ClientFor(member), cached))).StatusCode);
        var push = await EmptyPush(WithEpoch(f.ClientFor(member), cached));
        AssertReset(push.Status, push.Text, stored);

        // What the import does after its commit; from then on the new epoch is accepted everywhere.
        f.Services.GetRequiredService<DatasetEpochHandler>().Publish(stored);
        Assert.Equal(HttpStatusCode.OK, (await EmptyPush(WithEpoch(f.ClientFor(member), stored))).Status);
        Assert.Equal(stored, await ServerInfoEpoch(f));
    }
}

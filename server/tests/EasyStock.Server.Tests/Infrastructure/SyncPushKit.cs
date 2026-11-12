using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Handlers.Sync;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Server.Tests.Infrastructure;

/// <summary>Helpers of the sync push tests: seed rows, push a batch as a user, read rows back.</summary>
public static class SyncPushKit
{
    /// <summary>A unique text, as the tests share one database and its unique indexes.</summary>
    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 40)];

    public static HttpClient PushClient(this ApiFactory factory, SignedInUser? user) => factory.ClientFor(user);

    public static async Task SeedAsync(this WebApplicationFactory<Program> factory, params object[] rows)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.AddRange(rows);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<T> QueryAsync<T>(this WebApplicationFactory<Program> factory, Func<EasyStockDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<EasyStockDbContext>());
    }

    /// <summary>The wire row of an entity, as the app would send it.</summary>
    public static JsonObject Wire(object entity) => PushRowCodecHelper.Write(entity);

    public static JsonObject Body(Guid batchId, params (string Table, JsonObject Row)[] rows)
    {
        var tables = new JsonObject();
        foreach (var (table, row) in rows)
        {
            if (tables[table] is not JsonArray list) tables[table] = list = [];
            list.Add(row.DeepClone());
        }
        return new JsonObject { ["batchId"] = batchId.ToString(), ["rows"] = tables };
    }

    public sealed record PushAnswer(HttpStatusCode Status, string Text)
    {
        public JsonNode Json => JsonNode.Parse(Text)!;

        public JsonObject Result(string table, Guid id) => Result(table, id.ToString());

        public JsonObject Result(string table, string id) =>
            Json["results"]!.AsArray().Single(r => (string?)r!["table"] == table && (string?)r["id"] == id)!.AsObject();
    }

    public static async Task<PushAnswer> PushAsync(this HttpClient client, JsonObject body)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/sync/push", content, TestContext.Current.CancellationToken);
        return new PushAnswer(response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public static Task<PushAnswer> PushAsync(this HttpClient client, params (string Table, JsonObject Row)[] rows) =>
        client.PushAsync(Body(Guid.NewGuid(), rows));
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Server.Tests;

// Settings are global, so these tests must not run in parallel with each other or with anything that reads them.
[CollectionDefinition(nameof(AdminSettingsTests), DisableParallelization = true)]
public sealed class AdminSettingsCollection;

[Collection(nameof(AdminSettingsTests))]
public sealed class AdminSettingsTests(PostgresFixture pg) : IAsyncLifetime
{
    private const string Url = "/api/v1/admin/settings";

    private readonly ApiFactory _factory = new(pg.ConnectionString);

    // Each test starts and ends with no rows in `setting`, so the defaults apply.
    public async ValueTask InitializeAsync() => await ResetAsync();

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _factory.Dispose();
    }

    [Fact]
    public async Task Get_returns_defaults_when_nothing_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await (await Client(Role.Admin)).GetAsync(Url, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("EasyStock", doc.RootElement.GetProperty("serverName").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("movementAuthorRetentionDays").ValueKind);
    }

    [Fact]
    public async Task Put_then_get_returns_the_saved_values()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await Client(Role.Admin);

        var put = await client.PutAsJsonAsync(Url, new { serverName = "Maison Dupont", movementAuthorRetentionDays = 90 }, ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await client.GetFromJsonAsync<JsonElement>(Url, ct);
        Assert.Equal("Maison Dupont", get.GetProperty("serverName").GetString());
        Assert.Equal(90, get.GetProperty("movementAuthorRetentionDays").GetInt32());

        // null means keep everything, so a second PUT can clear the retention again.
        var clear = await client.PutAsJsonAsync(Url, new { serverName = "Maison Dupont", movementAuthorRetentionDays = (int?)null }, ct);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        get = await client.GetFromJsonAsync<JsonElement>(Url, ct);
        Assert.Equal(JsonValueKind.Null, get.GetProperty("movementAuthorRetentionDays").ValueKind);
    }

    [Fact]
    public async Task Name_of_exactly_50_characters_is_accepted()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = new string('x', 50);

        var put = await (await Client(Role.Admin)).PutAsJsonAsync(Url, new { serverName = name, movementAuthorRetentionDays = (int?)null }, ct);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    }

    [Fact]
    public async Task Name_of_51_characters_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = new string('x', 51);

        var put = await (await Client(Role.Admin)).PutAsJsonAsync(Url, new { serverName = name, movementAuthorRetentionDays = (int?)null }, ct);

        await AssertProblemAsync(put, HttpStatusCode.BadRequest, "validation-failed");
        Assert.Equal("EasyStock", await StoredServerNameAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_or_blank_name_is_rejected(string name)
    {
        var ct = TestContext.Current.CancellationToken;

        var put = await (await Client(Role.Admin)).PutAsJsonAsync(Url, new { serverName = name, movementAuthorRetentionDays = (int?)null }, ct);

        await AssertProblemAsync(put, HttpStatusCode.BadRequest, "validation-failed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Retention_below_one_day_is_rejected(int days)
    {
        var ct = TestContext.Current.CancellationToken;

        var put = await (await Client(Role.Admin)).PutAsJsonAsync(Url, new { serverName = "EasyStock", movementAuthorRetentionDays = days }, ct);

        await AssertProblemAsync(put, HttpStatusCode.BadRequest, "validation-failed");
    }

    [Fact]
    public async Task Unknown_key_is_rejected_with_validation_failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = """{"serverName":"EasyStock","movementAuthorRetentionDays":null,"theme":"dark"}""";

        var put = await (await Client(Role.Admin)).PutAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"), ct);

        await AssertProblemAsync(put, HttpStatusCode.BadRequest, "validation-failed");
    }

    [Fact]
    public async Task Wrong_type_is_rejected_with_validation_failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = """{"serverName":"EasyStock","movementAuthorRetentionDays":"ninety"}""";

        var put = await (await Client(Role.Admin)).PutAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"), ct);

        await AssertProblemAsync(put, HttpStatusCode.BadRequest, "validation-failed");
    }

    [Fact]
    public async Task Member_cannot_read_or_change_settings()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await Client(Role.Member);

        var get = await client.GetAsync(Url, ct);
        await AssertProblemAsync(get, HttpStatusCode.Forbidden, "not-allowed");

        var put = await client.PutAsJsonAsync(Url, new { serverName = "Hacked", movementAuthorRetentionDays = 1 }, ct);
        await AssertProblemAsync(put, HttpStatusCode.Forbidden, "not-allowed");
        Assert.Equal("EasyStock", await StoredServerNameAsync(ct));
    }

    private async Task<HttpClient> Client(Role role) => _factory.ClientFor(await _factory.SignInAsync(role));

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await db.Set<Setting>().ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> StoredServerNameAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var row = await db.Set<Setting>().AsNoTracking().SingleOrDefaultAsync(s => s.Key == "server_name", ct);
        return row is null ? "EasyStock" : JsonSerializer.Deserialize<string>(row.Value);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string type)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(status, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(type, doc.RootElement.GetProperty("type").GetString());
    }
}

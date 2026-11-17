using Xunit;

namespace EasyStock.Server.Tests.Platform;

public sealed class CompressionTests(PostgresFixture pg)
{
    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Json_answers_are_compressed_but_auth_answers_never_are()
    {
        using var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        var client = factory.CreateVersionedClient();

        var info = await GetAsync(client, "/api/v1/server-info");
        Assert.Contains("gzip", info.Content.Headers.ContentEncoding);

        var auth = await GetAsync(client, "/api/v1/auth/setup");
        Assert.Empty(auth.Content.Headers.ContentEncoding);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using EasyStock.Server.Enums;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class ImagesTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> ClientFor(Role role) => _factory.ClientFor(await _factory.SignInAsync(role));

    private static byte[] Jpeg(int size = 1000)
    {
        var bytes = RandomNumberGenerator.GetBytes(size);
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF;
        return bytes;
    }

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static ByteArrayContent Body(byte[] b, string type = "image/jpeg")
    {
        var c = new ByteArrayContent(b);
        c.Headers.ContentType = new MediaTypeHeaderValue(type);
        return c;
    }

    private static Task<HttpResponseMessage> Head(HttpClient c, string sha) =>
        c.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/v1/images/{sha}"), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Head_unknown_is_404()
    {
        var r = await Head(await ClientFor(Role.Member), Sha(Jpeg()));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Put_then_head_and_get_round_trip_with_cache_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Jpeg(); var sha = Sha(bytes);
        var put = await (await ClientFor(Role.Member)).PutAsync($"/api/v1/images/{sha}", Body(bytes), ct);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);

        var reader = await ClientFor(Role.Guest);
        Assert.Equal(HttpStatusCode.OK, (await Head(reader, sha)).StatusCode);

        var get = await reader.GetAsync($"/api/v1/images/{sha}", ct);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await get.Content.ReadAsByteArrayAsync(ct));
        Assert.Equal($"\"{sha}\"", get.Headers.ETag?.Tag);
        var cc = get.Headers.CacheControl!;
        Assert.True(cc.Private);
        Assert.Equal(TimeSpan.FromSeconds(31536000), cc.MaxAge);
        Assert.Contains("immutable", get.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Get_with_if_none_match_is_304()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Jpeg(); var sha = Sha(bytes);
        await (await ClientFor(Role.Admin)).PutAsync($"/api/v1/images/{sha}", Body(bytes), ct);
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/images/{sha}");
        req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{sha}\""));
        var r = await (await ClientFor(Role.Viewer)).SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NotModified, r.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_is_404()
    {
        var r = await (await ClientFor(Role.Member)).GetAsync($"/api/v1/images/{Sha(Jpeg())}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Put_twice_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Jpeg(); var sha = Sha(bytes);
        var c = await ClientFor(Role.Member);
        Assert.Equal(HttpStatusCode.Created, (await c.PutAsync($"/api/v1/images/{sha}", Body(bytes), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/v1/images/{sha}", Body(bytes), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Head(c, sha)).StatusCode);
    }

    [Fact]
    public async Task Put_hash_mismatch_is_400_and_nothing_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var sha = Sha(Jpeg());
        var c = await ClientFor(Role.Member);
        var r = await c.PutAsync($"/api/v1/images/{sha}", Body(Jpeg()), ct);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("hash-mismatch", await r.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.NotFound, (await Head(c, sha)).StatusCode);
    }

    [Fact]
    public async Task Put_too_large_is_413()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Jpeg(5 * 1024 * 1024 + 1);
        var r = await (await ClientFor(Role.Member)).PutAsync($"/api/v1/images/{Sha(bytes)}", Body(bytes), ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Contains("image-too-large", await r.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Put_at_the_limit_is_accepted()
    {
        var bytes = Jpeg(5 * 1024 * 1024);
        var r = await (await ClientFor(Role.Member)).PutAsync($"/api/v1/images/{Sha(bytes)}", Body(bytes), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    [Fact]
    public async Task Put_not_a_jpeg_is_400()
    {
        var bytes = RandomNumberGenerator.GetBytes(500);
        bytes[0] = 0x89; // PNG-like start
        var r = await (await ClientFor(Role.Member)).PutAsync($"/api/v1/images/{Sha(bytes)}", Body(bytes), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("not-a-jpeg", await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Put_wrong_content_type_is_415()
    {
        var bytes = Jpeg();
        var r = await (await ClientFor(Role.Member)).PutAsync($"/api/v1/images/{Sha(bytes)}", Body(bytes, "image/png"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
    }

    [Theory]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Guest)]
    public async Task Put_by_viewer_or_guest_is_403(Role role)
    {
        var bytes = Jpeg();
        var r = await (await ClientFor(role)).PutAsync($"/api/v1/images/{Sha(bytes)}", Body(bytes), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Jpeg(); var sha = Sha(bytes);
        var c = _factory.CreateVersionedClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync($"/api/v1/images/{sha}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Head(c, sha)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PutAsync($"/api/v1/images/{sha}", Body(bytes), ct)).StatusCode);
    }

    [Fact]
    public async Task Malformed_hash_is_404()
    {
        var r = await (await ClientFor(Role.Member)).GetAsync("/api/v1/images/not-a-hash", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}

using System.IO.Compression;
using System.Text.Json;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>Reading a backup archive without a database: limits, manifest versions, required columns, location order, JSON columns.</summary>
public sealed class BackupArchiveTests
{
    private static string NewId() => Guid.NewGuid().ToString("D");

    private static byte[] Manifest(int schemaVersion = 10) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
    {
        ["format"] = "easystock-backup", ["format_version"] = 1, ["app_version"] = "1.0.0", ["schema_version"] = schemaVersion,
        ["created_at"] = "2026-01-01T00:00:00Z",
    });

    private static MemoryStream Zip(params (string Name, byte[] Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                s.Write(content);
            }
        ms.Position = 0;
        return ms;
    }

    private static byte[] Table(int rows) =>
        JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0, rows).Select(_ => new Dictionary<string, object?>
        {
            ["id"] = NewId(), ["name"] = "Category " + Guid.NewGuid(), ["created_at"] = 1L, ["updated_at"] = 1L,
        }));

    private static byte[] Jpeg(int size)
    {
        var bytes = new byte[size];
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF;
        return bytes;
    }

    private static async Task<BackupError> Refusal(Stream file, BackupLimits? limits = null)
    {
        var e = await Assert.ThrowsAsync<BackupException>(() =>
            BackupArchive.OpenAsync(file, limits ?? BackupLimits.Default, TestContext.Current.CancellationToken));
        return e.Error;
    }

    // ---- size limits ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_table_over_its_limit_is_refused_as_too_large()
    {
        var table = Table(50);
        using var file = Zip(("manifest.json", Manifest()), ("tables/category.json", table));
        Assert.Equal(BackupError.TooLarge, await Refusal(file, new BackupLimits(TableBytes: table.Length - 1)));
    }

    [Fact]
    public async Task Tables_each_within_their_limit_but_over_the_total_are_refused()
    {
        var category = Table(50);
        var location = Table(50);
        var limits = new BackupLimits(TableBytes: category.Length + location.Length, TotalBytes: category.Length + location.Length / 2);
        using var file = Zip(("manifest.json", Manifest()), ("tables/category.json", category), ("tables/location.json", location));
        Assert.Equal(BackupError.TooLarge, await Refusal(file, limits));

        using var same = Zip(("manifest.json", Manifest()), ("tables/category.json", category), ("tables/location.json", location));
        using var ok = await BackupArchive.OpenAsync(same, limits with { TotalBytes = 10L * 1024 * 1024 }, TestContext.Current.CancellationToken);
        Assert.Equal(50, ok.Data.Categories.Count);
    }

    [Fact]
    public async Task Too_many_entries_are_refused_as_too_large()
    {
        var photos = Enumerable.Range(0, 20).Select(_ => ($"images/{NewId()}.jpg", Jpeg(10))).ToArray();
        using var file = Zip([("manifest.json", Manifest()), .. photos]);
        Assert.Equal(BackupError.TooLarge, await Refusal(file, new BackupLimits(Entries: 20)));
    }

    [Fact]
    public async Task Hashing_photos_counts_against_the_total_budget()
    {
        var ct = TestContext.Current.CancellationToken;
        var paths = Enumerable.Range(0, 3).Select(_ => $"images/{NewId()}.jpg").ToList();
        using var file = Zip([("manifest.json", Manifest()), .. paths.Select(p => (p, Jpeg(10_000)))]);
        using var archive = await BackupArchive.OpenAsync(file, new BackupLimits(TotalBytes: 25_000), ct);

        Assert.NotNull(await archive.HashPhotoAsync(paths[0], ct));
        Assert.NotNull(await archive.HashPhotoAsync(paths[1], ct));
        var e = await Assert.ThrowsAsync<BackupException>(() => archive.HashPhotoAsync(paths[2], ct));
        Assert.Equal(BackupError.TooLarge, e.Error);
    }

    [Fact]
    public async Task A_photo_larger_than_the_image_store_accepts_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(Handlers.Images.ImageHandler.MaxBytes, BackupFormatHelper.MaxImageBytes);
        var path = $"images/{NewId()}.jpg";
        using var file = Zip(("manifest.json", Manifest()), (path, Jpeg((int)BackupFormatHelper.MaxImageBytes + 1)));
        using var archive = await BackupArchive.OpenAsync(file, ct);
        var e = await Assert.ThrowsAsync<BackupException>(() => archive.HashPhotoAsync(path, ct));
        Assert.Equal(BackupError.TooLarge, e.Error);
    }

    // ---- manifest and rows ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_schema_version_below_the_oldest_supported_is_refused()
    {
        var oldest = CompatHelper.SupportedSchemaVersions.Min();
        using (var file = Zip(("manifest.json", Manifest(oldest - 1))))
            Assert.Equal(BackupError.Damaged, await Refusal(file));
        using (var file = Zip(("manifest.json", Manifest(oldest))))
            using (await BackupArchive.OpenAsync(file, TestContext.Current.CancellationToken)) { }
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("occurred_at")]
    [InlineData("created_at")]
    [InlineData("reason")]
    public async Task A_movement_without_a_required_column_is_refused(string column)
    {
        var row = new Dictionary<string, object?>
        {
            ["id"] = NewId(), ["article_id"] = NewId(), ["location_id"] = NewId(), ["quantity"] = 3L, ["reason"] = "purchase",
            ["occurred_at"] = 1000L, ["created_at"] = 1000L,
        };
        row.Remove(column);
        using var file = Zip(("manifest.json", Manifest()), ("tables/stock_movement.json", JsonSerializer.SerializeToUtf8Bytes(new[] { row })));
        Assert.Equal(BackupError.Damaged, await Refusal(file));
    }

    [Fact]
    public async Task Optional_columns_may_be_left_out()
    {
        var row = new Dictionary<string, object?>
        {
            ["id"] = NewId(), ["article_id"] = NewId(), ["location_id"] = NewId(), ["quantity"] = 3L, ["reason"] = "purchase",
            ["occurred_at"] = 1000L, ["created_at"] = 1000L,
        };
        using var file = Zip(("manifest.json", Manifest()), ("tables/stock_movement.json", JsonSerializer.SerializeToUtf8Bytes(new[] { row })));
        using var archive = await BackupArchive.OpenAsync(file, TestContext.Current.CancellationToken);
        var m = Assert.Single(archive.Data.Movements);
        Assert.Equal(3, m.Quantity);
        Assert.Null(m.Comment);
    }

    // ---- locations -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_very_deep_chain_of_locations_is_ordered_without_recursion()
    {
        const int depth = 200_000;
        var ids = Enumerable.Range(0, depth).Select(_ => NewId()).ToList();
        var chain = ids.Select((id, i) => new BackupLocation(id, "L" + i, 1, 1, ParentId: i == 0 ? null : ids[i - 1])).ToList();
        var shuffled = chain.OrderBy(_ => Random.Shared.Next()).ToList();

        var ordered = BackupLocationsHelper.ParentsFirst(shuffled);

        Assert.Equal(depth, ordered.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in ordered)
        {
            Assert.True(l.ParentId is null || seen.Contains(l.ParentId));
            seen.Add(l.Id);
        }
    }

    // ---- JSON columns --------------------------------------------------------------------------------------------

    private static BackupData WithAttributes(string json) =>
        new() { Articles = [new BackupArticle(NewId(), "A", 1, 1, Attributes: json)] };

    [Theory]
    [InlineData("""{"k":"\u0000"}""")]
    [InlineData("""{"k":"a\u0000b"}""")]
    [InlineData("""{"\u0000":"v"}""")]
    [InlineData("""{"k":"\\\u0000"}""")]
    public void A_NUL_character_in_a_JSON_column_is_refused(string json)
    {
        var e = Assert.Throws<BackupException>(() => BackupValidatorHelper.Validate(WithAttributes(json)));
        Assert.Equal(BackupError.Damaged, e.Error);
    }

    [Theory]
    [InlineData("""{"k":"\\u0000"}""")]
    [InlineData("""{"\\u0000":"v"}""")]
    [InlineData("""{"k":"é"}""")]
    public void An_escaped_backslash_before_u0000_is_not_a_NUL(string json) =>
        BackupValidatorHelper.Validate(WithAttributes(json));

    [Fact]
    public void Escapes_are_read_from_the_JSON_text_as_written()
    {
        // The C# value backslash + "u0000" serialises as an escaped backslash: what the app writes for that text.
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["k"] = "\\u0000" });
        Assert.Contains("\\\\u0000", json, StringComparison.Ordinal);
        BackupValidatorHelper.Validate(WithAttributes(json));
        Assert.Throws<BackupException>(() => BackupValidatorHelper.Validate(
            WithAttributes(JsonSerializer.Serialize(new Dictionary<string, string> { ["k"] = "\0" }))));
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Models;

/// <summary>
/// A plain backup archive opened for reading: manifest and tables are decoded and checked, the photos stay in the file
/// until read. Throws <see cref="BackupException"/> for anything that is not a readable backup. Entries are only ever
/// looked up by name, never extracted by path. Every read is bounded (<see cref="BackupLimits"/>): per entry, and in
/// total for the manifest, the tables and the hashing of the photos, so a small upload cannot expand without end.
/// </summary>
public sealed class BackupArchive : IDisposable
{
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] EncryptedMagic = "ESBK"u8.ToArray();
    private static readonly byte[] QrMagic = "ESQ1"u8.ToArray();

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _photos;
    private readonly BackupLimits _limits;
    private readonly ReadBudget _budget;

    private BackupArchive(ZipArchive zip, BackupManifest manifest, BackupData data, Dictionary<string, ZipArchiveEntry> photos,
        BackupLimits limits, ReadBudget budget)
    {
        _zip = zip;
        Manifest = manifest;
        Data = data;
        _photos = photos;
        _limits = limits;
        _budget = budget;
    }

    public BackupManifest Manifest { get; }

    /// <summary>The tables as they are in the file (not validated: see <see cref="BackupValidatorHelper"/>).</summary>
    public BackupData Data { get; }

    /// <summary>Photo paths present in the archive.</summary>
    public IReadOnlyCollection<string> PhotoPaths => _photos.Keys;

    public static Task<BackupArchive> OpenAsync(Stream file, CancellationToken ct) => OpenAsync(file, BackupLimits.Default, ct);

    public static async Task<BackupArchive> OpenAsync(Stream file, BackupLimits limits, CancellationToken ct)
    {
        var prefix = new byte[4];
        file.Position = 0;
        var n = await file.ReadAtLeastAsync(prefix, 4, throwOnEndOfStream: false, ct);
        file.Position = 0;
        if (n == 4 && prefix.AsSpan().SequenceEqual(EncryptedMagic)) throw new BackupException(BackupError.Encrypted, "Encrypted backup");
        if (n < 4 || !prefix.AsSpan().SequenceEqual(ZipMagic))
            throw new BackupException(BackupError.NotABackup, n == 4 && prefix.AsSpan().SequenceEqual(QrMagic) ? "QR backup" : "Not an archive");

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new BackupException(BackupError.Damaged, "Invalid zip");
        }

        try
        {
            IReadOnlyCollection<ZipArchiveEntry> all;
            try
            {
                all = zip.Entries;
            }
            catch (InvalidDataException)
            {
                throw new BackupException(BackupError.Damaged, "Invalid zip");
            }
            if (all.Count > limits.Entries) throw new BackupException(BackupError.TooLarge, "Too many entries");

            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var e in all)
                entries.TryAdd(e.FullName, e); // the first of a duplicated name wins, as ZipArchive.GetEntry does

            var budget = new ReadBudget(limits.TotalBytes);
            if (!entries.TryGetValue(BackupFormatHelper.ManifestEntry, out var manifestEntry))
                throw new BackupException(BackupError.NotABackup, "No manifest");
            var manifest = CheckManifest(await ReadAsync<BackupManifest>(manifestEntry, limits.ManifestBytes, budget, ct, notABackup: true));

            // Each table is decoded whole into a list (the merge needs all of it), bounded per table and, with the
            // rest of the archive, by the total budget.
            async Task<List<T>> Table<T>(BackupTable t) =>
                entries.TryGetValue(t.Entry(), out var e) ? await ReadAsync<List<T>>(e, limits.TableBytes, budget, ct) : [];

            var data = new BackupData
            {
                Categories = await Table<BackupCategory>(BackupTable.Category),
                AttributeDefinitions = await Table<BackupAttributeDefinition>(BackupTable.AttributeDefinition),
                CategoryAttributes = await Table<BackupCategoryAttribute>(BackupTable.CategoryAttribute),
                Locations = await Table<BackupLocation>(BackupTable.Location),
                Articles = await Table<BackupArticle>(BackupTable.Article),
                Items = await Table<BackupItem>(BackupTable.Item),
                Movements = await Table<BackupStockMovement>(BackupTable.StockMovement),
                Reviews = await Table<BackupStockReview>(BackupTable.StockReview),
                ReviewLines = await Table<BackupStockReviewLine>(BackupTable.StockReviewLine),
            };
            var photos = entries.Where(kv => BackupFormatHelper.IsValidImagePath(kv.Key) && !kv.Value.FullName.EndsWith('/'))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            return new BackupArchive(zip, manifest, data, photos, limits, budget);
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads a photo to compute its SHA-256 (lower-case hex) and checks it is a JPEG within the size limit (the image
    /// store's, so every photo that passes can be stored). Counts against the archive's total budget. Null when the
    /// archive has no such photo.
    /// </summary>
    public async Task<string?> HashPhotoAsync(string path, CancellationToken ct)
    {
        if (!_photos.TryGetValue(path, out var entry)) return null;
        try
        {
            await using var stream = new LimitedStream(await entry.OpenAsync(ct), _limits.ImageBytes, _budget);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            var first = true;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (first)
                {
                    if (read < 3 || buffer[0] != 0xFF || buffer[1] != 0xD8 || buffer[2] != 0xFF)
                        throw new BackupException(BackupError.Damaged, "A photo is not a JPEG");
                    first = false;
                }
                hash.AppendData(buffer, 0, read);
            }
            if (first) throw new BackupException(BackupError.Damaged, "Empty photo");
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (InvalidDataException)
        {
            throw new BackupException(BackupError.Damaged, "Invalid zip entry");
        }
    }

    /// <summary>
    /// Opens a photo of the archive (bounded per photo). Null when missing. Meant for a photo already checked by
    /// <see cref="HashPhotoAsync"/>, whose bytes were counted then, so this second read is not counted again.
    /// </summary>
    public async Task<Stream?> OpenPhotoAsync(string path, CancellationToken ct) =>
        _photos.TryGetValue(path, out var entry) ? new LimitedStream(await entry.OpenAsync(ct), _limits.ImageBytes) : null;

    public void Dispose() => _zip.Dispose();

    private static BackupManifest CheckManifest(BackupManifest m)
    {
        if (m.Format != BackupFormatHelper.Format) throw new BackupException(BackupError.NotABackup, "Unknown format");
        if (m.FormatVersion < 1 || m.SchemaVersion < 1) throw new BackupException(BackupError.Damaged, "Invalid version");
        if (m.FormatVersion > BackupFormatHelper.FormatVersion) throw new BackupException(BackupError.NewerVersion, "Newer format");
        if (m.SchemaVersion > BackupFormatHelper.MaxImportSchemaVersion) throw new BackupException(BackupError.NewerVersion, "Newer schema");
        // An older app's tables are not the ones the server matches: refused rather than read with another shape.
        if (m.SchemaVersion < BackupFormatHelper.MinImportSchemaVersion) throw new BackupException(BackupError.Damaged, "Unsupported older schema");
        return m;
    }

    private static async Task<T> ReadAsync<T>(ZipArchiveEntry entry, long maxBytes, ReadBudget budget, CancellationToken ct, bool notABackup = false)
    {
        if (entry.FullName.EndsWith('/')) throw new BackupException(BackupError.Damaged, "Directory entry");
        try
        {
            // The size in the entry is not trusted: the stream is cut at the limit.
            await using var stream = new LimitedStream(await entry.OpenAsync(ct), maxBytes, budget);
            return await JsonSerializer.DeserializeAsync<T>(stream, BackupFormatHelper.Json, ct)
                   ?? throw new BackupException(BackupError.Damaged, "Empty table");
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new BackupException(notABackup ? BackupError.NotABackup : BackupError.Damaged, "Invalid JSON");
        }
    }
}

/// <summary>Decompressed bytes an archive may still be read for, shared by its bounded streams.</summary>
internal sealed class ReadBudget(long total)
{
    private long _remaining = total;

    public void Take(int n)
    {
        _remaining -= n;
        if (_remaining < 0) throw new BackupException(BackupError.TooLarge, "Archive too large");
    }
}

/// <summary>
/// Read-only stream that throws <see cref="BackupException"/> (TooLarge) once more than a limit has been read, or once
/// the shared budget of its archive runs out.
/// </summary>
internal sealed class LimitedStream(Stream inner, long limit, ReadBudget? budget = null) : Stream
{
    private long _read;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    private int Count(int n)
    {
        _read += n;
        if (_read > limit) throw new BackupException(BackupError.TooLarge, "Entry too large");
        budget?.Take(n);
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        Count(await inner.ReadAsync(buffer, ct));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }
}

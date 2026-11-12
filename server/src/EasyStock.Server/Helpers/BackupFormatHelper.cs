using System.Text.Json;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Helpers;

// The app's backup archive (shared/src/commonMain/.../backup/BackupFormat.kt in the app repository), a zip:
//   manifest.json         BackupManifest
//   tables/<table>.json   JSON array of rows, columns named as in docs/schema.dbml
//   images/<uuid>.jpg     article photos, same relative path as article.image_path
// The server reads and writes the plain archive. An encrypted one (magic "ESBK") and the QR variant ("ESQ1")
// are refused: the server has no password to open them.

public static class BackupFormatHelper
{
    public const string Format = "easystock-backup";
    public const int FormatVersion = 1;

    /// <summary>
    /// Schema version written in exported backups: the lowest supported one, so every supported app can read them
    /// (an app refuses a backup newer than its own DB_VERSION). The data shape is the same for all supported versions.
    /// </summary>
    public static int ExportSchemaVersion => CompatHelper.SupportedSchemaVersions.Min();

    /// <summary>Highest schema version accepted when importing a backup: the newest supported app's.</summary>
    public static int MaxImportSchemaVersion => CompatHelper.SupportedSchemaVersions.Max();

    public const string FileExtension = "easystock";
    public const string ManifestEntry = "manifest.json";
    public const string ImagesDir = "images";

    public const long MaxManifestBytes = 64L * 1024;
    /// <summary>The app limits a table to 64 MB; the server reads it as a stream and accepts more (a long history of movements).</summary>
    public const long MaxTableBytes = 1024L * 1024 * 1024;
    public const long MaxImageBytes = 32L * 1024 * 1024;

    /// <summary>Upload limit of <c>POST /admin/backup</c> only (docs/04, Phase 11).</summary>
    public const long MaxUploadBytes = 500L * 1024 * 1024;

    public static readonly JsonSerializerOptions Json = new()
    {
        // The app writes every column, null ones included, and reads with the defaults of its entities.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = false,
    };

    /// <summary>Photo path of the archive for a stored photo: "images/" + the first 128 bits of its SHA-256 as a UUID + ".jpg".</summary>
    public static string ImagePathFor(string sha256)
    {
        var h = sha256;
        return $"{ImagesDir}/{h[..8]}-{h[8..12]}-{h[12..16]}-{h[16..20]}-{h[20..32]}.jpg";
    }

    public static bool IsValidImagePath(string path) =>
        path.Length == ImagesDir.Length + 1 + 36 + 4
        && path.StartsWith(ImagesDir + "/", StringComparison.Ordinal)
        && path.EndsWith(".jpg", StringComparison.Ordinal)
        && IsLowerUuid(path.AsSpan(ImagesDir.Length + 1, 36));

    public static bool IsLowerUuid(ReadOnlySpan<char> s)
    {
        if (s.Length != 36) return false;
        for (var i = 0; i < 36; i++)
        {
            var c = s[i];
            if (i is 8 or 13 or 18 or 23) { if (c != '-') return false; }
            else if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        }
        return true;
    }
}

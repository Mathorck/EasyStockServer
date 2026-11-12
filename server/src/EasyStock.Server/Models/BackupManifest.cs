using System.Text.Json.Serialization;

namespace EasyStock.Server.Models;

/// <summary><c>manifest.json</c> of a backup archive (the app's <c>BackupManifest</c>).</summary>
public sealed record BackupManifest(
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("format_version")] int FormatVersion,
    [property: JsonPropertyName("app_version")] string AppVersion,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("counts")] Dictionary<string, int>? Counts = null);

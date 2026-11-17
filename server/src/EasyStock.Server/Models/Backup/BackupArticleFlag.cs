using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Backup;

public sealed record BackupArticleFlag(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("article_id")] string ArticleId,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("created_by")] string? CreatedBy = null,
    [property: JsonPropertyName("resolved_at")] long? ResolvedAt = null,
    [property: JsonPropertyName("deleted_at")] long? DeletedAt = null);

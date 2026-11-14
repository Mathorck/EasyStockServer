namespace EasyStock.Server.DB.Entities;

/// <summary>
/// A push batch already applied, with its answer: the same batch sent again gets the same bytes back and changes
/// nothing (docs/04-server-implementation-plan.md, 3.2). Deleted after 7 days by the cleanup job.
/// </summary>
public sealed class SyncBatch
{
    public Guid BatchId { get; set; }

    /// <summary>The user who pushed it. A different user with the same batch ID gets 409 batch-id-reused.</summary>
    public Guid UserId { get; set; }

    public Guid DeviceSessionId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>The answer exactly as sent (text, not jsonb, so a replay is byte-identical).</summary>
    public string Response { get; set; } = "";

    /// <summary>
    /// SHA-256 of the batch's rows: the same batch ID sent again with other rows gets 409 batch-id-reused instead of
    /// the saved answer. Null on batches saved before the hash existed (never compared).
    /// </summary>
    public byte[]? RequestHash { get; set; }
}

/// <summary>
/// An attribute_definition ID that a push merged into the server attribute with the same key (ATTRIBUTE_MERGED): a
/// later batch that still uses the phone's old ID is redirected to the server one.
/// </summary>
public sealed class AttributeAlias
{
    /// <summary>The phone's ID, not in attribute_definition.</summary>
    public Guid AliasId { get; set; }

    /// <summary>The server attribute it was merged into.</summary>
    public Guid AttributeId { get; set; }
}

/// <summary>
/// Whether an article is at or below its threshold, to alert only when it crosses it. No row means "not low";
/// an article that can't alert (serial, archived, deleted, no threshold) has no row.
/// </summary>
public sealed class LowStockState
{
    public Guid ArticleId { get; set; }

    public bool IsLow { get; set; }
}

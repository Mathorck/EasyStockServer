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

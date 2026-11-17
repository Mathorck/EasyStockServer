namespace EasyStock.Server.DB.Entities;

/// <summary>Append-only: never updated or deleted.</summary>
public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid LocationId { get; set; }
    public long Quantity { get; set; }
    public string Reason { get; set; } = "";
    public string? Comment { get; set; }
    public long OccurredAt { get; set; }
    public long CreatedAt { get; set; }
    /// <summary>Set by the server from the access token, never from the phone.</summary>
    public Guid? CreatedBy { get; set; }
    /// <summary>Set by the server from the access token, never from the phone.</summary>
    public Guid? DeviceId { get; set; }
    public long ServerSeq { get; set; }
}

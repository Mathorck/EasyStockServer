namespace EasyStock.Server.DB.Entities;

/// <summary>Shared by sync push (writes) and sync pull (reads). Pulled like the shared tables, by server_seq.</summary>
public sealed class LowStockAlert
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    /// <summary>Epoch ms UTC, when the stock went below the threshold.</summary>
    public long CrossedAt { get; set; }
    /// <summary>Stock at that moment, in thousandths.</summary>
    public long Stock { get; set; }
    public Guid? CausedByUserId { get; set; }
    public long ServerSeq { get; set; }
}

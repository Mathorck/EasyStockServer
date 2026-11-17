namespace EasyStock.Server.DB.Entities;

public sealed class StockReview
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string Status { get; set; } = "open";
    public long StartedAt { get; set; }
    public long? FinishedAt { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

namespace EasyStock.Server.DB.Entities;

public sealed class StockReviewLine
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public Guid ArticleId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid LocationId { get; set; }
    public long ExpectedQuantity { get; set; }
    public long? CountedQuantity { get; set; }
    public string Result { get; set; } = "unchecked";
    public Guid? FoundLocationId { get; set; }
    public bool InfoFlagged { get; set; }
    public string? Note { get; set; }
    public long? InfoFixedAt { get; set; }
    public string? PendingItem { get; set; }
    public long? CheckedAt { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

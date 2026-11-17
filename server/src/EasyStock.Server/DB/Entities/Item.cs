namespace EasyStock.Server.DB.Entities;

public sealed class Item
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public string? SerialNumber { get; set; }
    public string Status { get; set; } = "available";
    public Guid? LocationId { get; set; }
    public string? Attributes { get; set; }
    public bool Archived { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

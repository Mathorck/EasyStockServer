namespace EasyStock.Server.DB.Entities;

public sealed class Article
{
    public Guid Id { get; set; }
    public string? Sku { get; set; }
    public string Name { get; set; } = "";
    public bool NameAuto { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public string Tracking { get; set; } = "quantity";
    public string Unit { get; set; } = "pce";
    public string? Barcode { get; set; }
    public long? Threshold { get; set; }
    public string? ImageSha256 { get; set; }
    public string? Attributes { get; set; }
    public string? OrderUrl { get; set; }
    public bool Archived { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

namespace EasyStock.Server.DB.Entities;

public sealed class Location
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? Barcode { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

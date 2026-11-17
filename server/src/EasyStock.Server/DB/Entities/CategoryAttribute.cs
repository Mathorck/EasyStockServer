namespace EasyStock.Server.DB.Entities;

public sealed class CategoryAttribute
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public Guid AttributeId { get; set; }
    public bool Required { get; set; }
    public int SortOrder { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

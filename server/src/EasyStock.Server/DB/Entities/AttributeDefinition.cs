namespace EasyStock.Server.DB.Entities;

public sealed class AttributeDefinition
{
    public Guid Id { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string DataType { get; set; } = "";
    public string? Unit { get; set; }
    public string? Options { get; set; }
    public bool FlagWhenPast { get; set; }
    public bool NotifyWhenPast { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

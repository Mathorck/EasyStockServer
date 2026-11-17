namespace EasyStock.Server.DB.Entities;

/// <summary>A note raised on an article ("something is wrong"). Any member can raise one, only members resolve it.</summary>
public sealed class ArticleFlag
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public string Note { get; set; } = "";
    public long CreatedAt { get; set; }
    /// <summary>Set by the server from the access token on insert, never from the phone, kept on updates.</summary>
    public Guid? CreatedBy { get; set; }
    public long? ResolvedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

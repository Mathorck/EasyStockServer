namespace EasyStock.Server.DB.Entities;

/// <summary>Server-only table: one row per photo stored under DATA_DIR/images. Not synced.</summary>
public sealed class ImageRecord
{
    /// <summary>Lowercase hex SHA-256 of the JPEG bytes.</summary>
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    /// <summary>Epoch ms UTC.</summary>
    public long CreatedAt { get; set; }
}

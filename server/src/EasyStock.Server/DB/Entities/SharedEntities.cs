namespace EasyStock.Server.DB.Entities;

// The nine tables shared with the app. Source of truth: docs/schema.dbml in the app repository.
// Server rules (docs/03-server-spec.md, "Shared tables"):
//   - timestamps are bigint epoch milliseconds UTC, like the app;
//   - decimal(12,3) quantities are bigint thousandths (1.5 kg = 1500), like the app;
//   - json columns are jsonb, kept as raw JSON text;
//   - every table has server_seq; article.image_path is image_sha256 on the server.
// SchemaDriftTests compares the migrated database against schema.dbml.

public sealed class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? NameTemplate { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

public sealed class AttributeDefinition
{
    public Guid Id { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string DataType { get; set; } = "";
    public string? Unit { get; set; }
    public string? Options { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

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
    public bool Archived { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }
    public long ServerSeq { get; set; }
}

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

/// <summary>Append-only: never updated or deleted.</summary>
public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid LocationId { get; set; }
    public long Quantity { get; set; }
    public string Reason { get; set; } = "";
    public string? Comment { get; set; }
    public long OccurredAt { get; set; }
    public long CreatedAt { get; set; }
    /// <summary>Set by the server from the access token, never from the phone.</summary>
    public Guid? CreatedBy { get; set; }
    /// <summary>Set by the server from the access token, never from the phone.</summary>
    public Guid? DeviceId { get; set; }
    public long ServerSeq { get; set; }
}

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

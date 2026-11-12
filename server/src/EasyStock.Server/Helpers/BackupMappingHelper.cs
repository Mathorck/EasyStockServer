using EasyStock.Server.DB.Entities;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Helpers;

/// <summary>
/// Entity to backup row and back. <c>article.image_sha256</c> is the archive's <c>image_path</c>: the path is derived
/// from the hash (<see cref="BackupFormatHelper.ImagePathFor"/>), the reverse goes through a path-to-hash index.
/// </summary>
internal static class BackupMappingHelper
{
    private static string Id(System.Guid g) => g.ToString("D");
    private static string? Id(System.Guid? g) => g?.ToString("D");
    private static System.Guid G(string s) => System.Guid.ParseExact(s, "D");
    private static System.Guid? N(string? s) => s is null ? null : System.Guid.ParseExact(s, "D");

    public static BackupCategory ToRow(this Category e) =>
        new(Id(e.Id), e.Name, e.CreatedAt, e.UpdatedAt, e.Description, e.NameTemplate, e.DeletedAt);

    public static BackupAttributeDefinition ToRow(this AttributeDefinition e) =>
        new(Id(e.Id), e.Key, e.Label, e.DataType, e.CreatedAt, e.UpdatedAt, e.Unit, e.Options, e.DeletedAt);

    public static BackupCategoryAttribute ToRow(this CategoryAttribute e) =>
        new(Id(e.Id), Id(e.CategoryId), Id(e.AttributeId), e.CreatedAt, e.UpdatedAt, e.Required, e.SortOrder, e.DeletedAt);

    public static BackupLocation ToRow(this Location e) =>
        new(Id(e.Id), e.Name, e.CreatedAt, e.UpdatedAt, Id(e.ParentId), e.Icon, e.Color, e.Barcode, e.DeletedAt);

    public static BackupArticle ToRow(this Article e) =>
        new(Id(e.Id), e.Name, e.CreatedAt, e.UpdatedAt, e.Sku, e.NameAuto, e.Description, Id(e.CategoryId), e.Tracking, e.Unit,
            e.Barcode, e.Threshold, e.ImageSha256 is null ? null : BackupFormatHelper.ImagePathFor(e.ImageSha256), e.Attributes, e.Archived, e.DeletedAt);

    public static BackupItem ToRow(this Item e) =>
        new(Id(e.Id), Id(e.ArticleId), e.CreatedAt, e.UpdatedAt, e.SerialNumber, e.Status, Id(e.LocationId), e.Attributes, e.Archived, e.DeletedAt);

    public static BackupStockMovement ToRow(this StockMovement e) =>
        new(Id(e.Id), Id(e.ArticleId), Id(e.LocationId), e.Quantity, e.Reason, e.OccurredAt, e.CreatedAt, Id(e.ItemId), e.Comment,
            Id(e.CreatedBy), Id(e.DeviceId));

    public static BackupStockReview ToRow(this StockReview e) =>
        new(Id(e.Id), Id(e.LocationId), e.StartedAt, e.CreatedAt, e.UpdatedAt, e.Status, e.FinishedAt, e.DeletedAt);

    public static BackupStockReviewLine ToRow(this StockReviewLine e) =>
        new(Id(e.Id), Id(e.ReviewId), Id(e.ArticleId), Id(e.LocationId), e.ExpectedQuantity, e.CreatedAt, e.UpdatedAt, Id(e.ItemId),
            e.CountedQuantity, e.Result, Id(e.FoundLocationId), e.InfoFlagged, e.Note, e.InfoFixedAt, e.PendingItem, e.CheckedAt, e.DeletedAt);

    // Rows are validated before they get here (UUID ids), so ParseExact cannot fail.

    public static Category ToEntity(this BackupCategory r) => new()
    {
        Id = G(r.Id), Name = r.Name, Description = r.Description, NameTemplate = r.NameTemplate,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    public static AttributeDefinition ToEntity(this BackupAttributeDefinition r) => new()
    {
        Id = G(r.Id), Key = r.Key, Label = r.Label, DataType = r.DataType, Unit = r.Unit, Options = r.Options,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    public static CategoryAttribute ToEntity(this BackupCategoryAttribute r) => new()
    {
        Id = G(r.Id), CategoryId = G(r.CategoryId), AttributeId = G(r.AttributeId), Required = r.Required, SortOrder = r.SortOrder,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    public static Location ToEntity(this BackupLocation r) => new()
    {
        Id = G(r.Id), Name = r.Name, ParentId = N(r.ParentId), Icon = r.Icon, Color = r.Color, Barcode = r.Barcode,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    public static Article ToEntity(this BackupArticle r, IReadOnlyDictionary<string, string> imageHashes) => new()
    {
        Id = G(r.Id), Sku = r.Sku, Name = r.Name, NameAuto = r.NameAuto, Description = r.Description, CategoryId = N(r.CategoryId),
        Tracking = r.Tracking, Unit = r.Unit, Barcode = r.Barcode, Threshold = r.Threshold,
        ImageSha256 = r.ImagePath is not null && imageHashes.TryGetValue(r.ImagePath, out var sha) ? sha : null,
        Attributes = r.Attributes, Archived = r.Archived, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    public static Item ToEntity(this BackupItem r) => new()
    {
        Id = G(r.Id), ArticleId = G(r.ArticleId), SerialNumber = r.SerialNumber, Status = r.Status, LocationId = N(r.LocationId),
        Attributes = r.Attributes, Archived = r.Archived, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    /// <summary>
    /// <c>created_by</c> and <c>device_id</c> are not kept: they name users and devices of the server the backup came from,
    /// and nothing guarantees they exist here (personal data kept to a minimum).
    /// </summary>
    public static StockMovement ToEntity(this BackupStockMovement r) => new()
    {
        Id = G(r.Id), ArticleId = G(r.ArticleId), ItemId = N(r.ItemId), LocationId = G(r.LocationId), Quantity = r.Quantity,
        Reason = r.Reason, Comment = r.Comment, OccurredAt = r.OccurredAt, CreatedAt = r.CreatedAt,
    };

    public static StockReview ToEntity(this BackupStockReview r) => new()
    {
        Id = G(r.Id), LocationId = G(r.LocationId), Status = r.Status, StartedAt = r.StartedAt, FinishedAt = r.FinishedAt,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    public static StockReviewLine ToEntity(this BackupStockReviewLine r) => new()
    {
        Id = G(r.Id), ReviewId = G(r.ReviewId), ArticleId = G(r.ArticleId), ItemId = N(r.ItemId), LocationId = G(r.LocationId),
        ExpectedQuantity = r.ExpectedQuantity, CountedQuantity = r.CountedQuantity, Result = r.Result, FoundLocationId = N(r.FoundLocationId),
        InfoFlagged = r.InfoFlagged, Note = r.Note, InfoFixedAt = r.InfoFixedAt, PendingItem = r.PendingItem, CheckedAt = r.CheckedAt,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    public static System.Guid ParseId(string id) => G(id);
}

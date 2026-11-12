using System.Text.Json;
using EasyStock.Server.DB.Entities;

namespace EasyStock.Server.Helpers;

/// <summary>
/// Values a pushed row may hold: enums, column lengths (DB/Configurations), required text, quantity rules and the
/// shape of the jsonb columns. References are checked by <see cref="PushHandler"/> against the database.
/// </summary>
public static class PushRowValidatorHelper
{
    public static readonly string[] DataTypes = ["text", "number", "bool", "date", "enum", "measure"];
    public static readonly string[] Trackings = ["quantity", "serial"];
    public static readonly string[] ItemStatuses = ["available", "lent", "repair", "lost"];
    public static readonly string[] Reasons = ["purchase", "consumption", "inventory", "transfer", "loan", "return", "loss"];
    public static readonly string[] ReviewStatuses = ["open", "finished", "cancelled"];
    public static readonly string[] LineResults = ["unchecked", "counted", "present", "missing", "elsewhere"];

    public const int CategoryNameMax = 100;
    public const int SkuMax = 50;
    public const int SerialMax = 100;

    public static bool IsValid(object row) => row switch
    {
        Category c => Times(c.CreatedAt, c.UpdatedAt, c.DeletedAt) && Text(c.Name, CategoryNameMax),
        AttributeDefinition d => Times(d.CreatedAt, d.UpdatedAt, d.DeletedAt) && Text(d.Key, 50) && Text(d.Label, 100)
            && DataTypes.Contains(d.DataType) && Optional(d.Unit, 20) && Json(d.Options, JsonValueKind.Array),
        CategoryAttribute ca => Times(ca.CreatedAt, ca.UpdatedAt, ca.DeletedAt) && ca.SortOrder >= 0,
        Location l => Times(l.CreatedAt, l.UpdatedAt, l.DeletedAt) && Text(l.Name, 100) && Optional(l.Icon, 32)
            && Optional(l.Color, 32) && Optional(l.Barcode, 64),
        Article a => Times(a.CreatedAt, a.UpdatedAt, a.DeletedAt) && Optional(a.Sku, SkuMax) && Text(a.Name, 255)
            && Trackings.Contains(a.Tracking) && Text(a.Unit, 20) && Optional(a.Barcode, 64)
            && a.Threshold is null or >= 0 && Sha256(a.ImageSha256) && Json(a.Attributes, JsonValueKind.Object),
        Item i => Times(i.CreatedAt, i.UpdatedAt, i.DeletedAt) && Optional(i.SerialNumber, SerialMax)
            && ItemStatuses.Contains(i.Status) && Json(i.Attributes, JsonValueKind.Object),
        StockMovement m => Times(m.CreatedAt, m.OccurredAt, null) && Reasons.Contains(m.Reason),
        StockReview r => Times(r.CreatedAt, r.UpdatedAt, r.DeletedAt) && r.StartedAt >= 0 && r.FinishedAt is null or >= 0
            && ReviewStatuses.Contains(r.Status),
        StockReviewLine x => Times(x.CreatedAt, x.UpdatedAt, x.DeletedAt) && x.ExpectedQuantity >= 0
            && x.CountedQuantity is null or >= 0 && LineResults.Contains(x.Result) && x.InfoFixedAt is null or >= 0
            && x.CheckedAt is null or >= 0 && Json(x.PendingItem, JsonValueKind.Object),
        _ => false,
    };

    private static bool Times(long created, long updated, long? deleted) => created >= 0 && updated >= 0 && deleted is null or >= 0;

    private static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;

    private static bool Optional(string? value, int max) => value is null || (value.Length > 0 && value.Length <= max);

    private static bool Sha256(string? value) =>
        value is null || (value.Length == 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));

    private static bool Json(string? raw, JsonValueKind kind)
    {
        if (raw is null) return true;
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.ValueKind == kind;
    }
}

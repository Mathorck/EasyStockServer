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

    /// <summary>The app's decimal(12,3) as thousandths: 999,999,999.999. Keeps SUM(quantity) far from bigint overflow.</summary>
    public const long QuantityMax = 999_999_999_999;

    public static bool IsValid(object row) => row switch
    {
        Category c => Times(c.CreatedAt, c.UpdatedAt, c.DeletedAt) && Text(c.Name, CategoryNameMax)
            && FreeText(c.Description) && FreeText(c.NameTemplate),
        AttributeDefinition d => Times(d.CreatedAt, d.UpdatedAt, d.DeletedAt) && Text(d.Key, 50) && Text(d.Label, 100)
            && DataTypes.Contains(d.DataType) && Optional(d.Unit, 20) && Json(d.Options, JsonValueKind.Array),
        CategoryAttribute ca => Times(ca.CreatedAt, ca.UpdatedAt, ca.DeletedAt) && ca.SortOrder >= 0,
        Location l => Times(l.CreatedAt, l.UpdatedAt, l.DeletedAt) && Text(l.Name, 100) && Optional(l.Icon, 32)
            && Optional(l.Color, 32) && Optional(l.Barcode, 64),
        Article a => Times(a.CreatedAt, a.UpdatedAt, a.DeletedAt) && Optional(a.Sku, SkuMax) && Text(a.Name, 255)
            && Trackings.Contains(a.Tracking) && Text(a.Unit, 20) && Optional(a.Barcode, 64)
            && Threshold(a.Threshold) && FreeText(a.Description) && Sha256(a.ImageSha256) && Json(a.Attributes, JsonValueKind.Object),
        Item i => Times(i.CreatedAt, i.UpdatedAt, i.DeletedAt) && Optional(i.SerialNumber, SerialMax)
            && ItemStatuses.Contains(i.Status) && Json(i.Attributes, JsonValueKind.Object),
        StockMovement m => Times(m.CreatedAt, m.OccurredAt, null) && Reasons.Contains(m.Reason) && MovementQuantity(m.Quantity)
            && FreeText(m.Comment),
        StockReview r => Times(r.CreatedAt, r.UpdatedAt, r.DeletedAt) && r.StartedAt >= 0 && r.FinishedAt is null or >= 0
            && ReviewStatuses.Contains(r.Status),
        StockReviewLine x => Times(x.CreatedAt, x.UpdatedAt, x.DeletedAt) && x.ExpectedQuantity is >= 0 and <= QuantityMax
            && x.CountedQuantity is null or (>= 0 and <= QuantityMax) && FreeText(x.Note) && LineResults.Contains(x.Result) && x.InfoFixedAt is null or >= 0
            && x.CheckedAt is null or >= 0 && Json(x.PendingItem, JsonValueKind.Object),
        _ => false,
    };

    private static bool Times(long created, long updated, long? deleted) => created >= 0 && updated >= 0 && deleted is null or >= 0;

    // PostgreSQL text and jsonb reject U+0000, which would otherwise surface as a 500.
    private static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !HasNul(value);

    private static bool Optional(string? value, int max) => value is null || (value.Length > 0 && value.Length <= max && !HasNul(value));

    private static bool FreeText(string? value) => value is null || !HasNul(value);

    private static bool HasNul(string value) => value.Contains('\0');

    /// <summary>Movements are signed (consumption out, purchase in) but never zero, and bounded.</summary>
    private static bool MovementQuantity(long quantity) => quantity != 0 && quantity is >= -QuantityMax and <= QuantityMax;

    private static bool Threshold(long? value) => value is null or (>= 0 and <= QuantityMax);

    private static bool Sha256(string? value) =>
        value is null || (value.Length == 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));

    private static bool Json(string? raw, JsonValueKind kind)
    {
        if (raw is null) return true;
        using var doc = JsonDocument.Parse(raw);
        if (raw.Contains("\\u0000", StringComparison.OrdinalIgnoreCase)) return false; // escaped NUL, rejected by jsonb
        return doc.RootElement.ValueKind == kind;
    }
}

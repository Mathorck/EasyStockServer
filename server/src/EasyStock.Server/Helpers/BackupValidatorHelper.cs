using System.Text;
using System.Text.Json;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Helpers;

/// <summary>
/// Checks that a backup is consistent before anything is written: the app's <c>validateBackup</c> (unique ids and
/// unique columns, every reference points to a row of the backup, no location cycle, valid photo paths), plus what
/// the PostgreSQL schema needs (UUID ids, enum values, column lengths, JSON shapes, no NUL characters). An import
/// never fails half-way on a constraint. Throws <see cref="BackupException"/> (Damaged) without row content.
/// </summary>
public static class BackupValidatorHelper
{
    private static readonly HashSet<string> DataTypes = ["text", "number", "bool", "date", "enum", "measure"];
    private static readonly HashSet<string> Trackings = ["quantity", "serial"];
    private static readonly HashSet<string> ItemStatuses = ["available", "lent", "repair", "lost"];
    private static readonly HashSet<string> Reasons = ["purchase", "consumption", "inventory", "transfer", "loan", "return", "loss"];
    private static readonly HashSet<string> ReviewStatuses = ["open", "finished", "cancelled"];
    private static readonly HashSet<string> ReviewResults = ["unchecked", "counted", "present", "missing", "elsewhere"];

    public static void Validate(BackupData data)
    {
        var categories = Ids(data.Categories, x => x.Id, "category");
        var attributes = Ids(data.AttributeDefinitions, x => x.Id, "attribute_definition");
        Ids(data.CategoryAttributes, x => x.Id, "category_attribute");
        var locations = Ids(data.Locations, x => x.Id, "location");
        var articles = Ids(data.Articles, x => x.Id, "article");
        var items = Ids(data.Items, x => x.Id, "item");
        Ids(data.Movements, x => x.Id, "stock_movement");
        var reviews = Ids(data.Reviews, x => x.Id, "stock_review");
        Ids(data.ReviewLines, x => x.Id, "stock_review_line");

        foreach (var r in data.Categories) { Text(r.Name, 100); Text(r.Description, int.MaxValue); Text(r.NameTemplate, int.MaxValue); }
        foreach (var r in data.AttributeDefinitions)
        {
            Text(r.Key, 50); Text(r.Label, 100); Text(r.DataType, 20); Text(r.Unit, 20);
            if (!DataTypes.Contains(r.DataType)) Damaged("Unknown attribute data type");
            Json(r.Options, '[');
        }
        foreach (var r in data.Locations)
        {
            Text(r.Name, 100); Text(r.Icon, 32); Text(r.Color, 32); Text(r.Barcode, 64);
        }
        foreach (var r in data.Articles)
        {
            Text(r.Sku, 50); Text(r.Name, 255); Text(r.Description, int.MaxValue); Text(r.Tracking, 10); Text(r.Unit, 20); Text(r.Barcode, 64);
            if (!Trackings.Contains(r.Tracking)) Damaged("Unknown tracking");
            Json(r.Attributes, '{');
        }
        foreach (var r in data.Items)
        {
            Text(r.SerialNumber, 100); Text(r.Status, 20);
            if (!ItemStatuses.Contains(r.Status)) Damaged("Unknown item status");
            Json(r.Attributes, '{');
        }
        foreach (var r in data.Movements)
        {
            Text(r.Reason, 30); Text(r.Comment, int.MaxValue);
            if (!Reasons.Contains(r.Reason)) Damaged("Unknown movement reason");
            if (r.CreatedBy is not null) Uuid(r.CreatedBy);
            if (r.DeviceId is not null) Uuid(r.DeviceId);
        }
        foreach (var r in data.Reviews)
        {
            Text(r.Status, 20);
            if (!ReviewStatuses.Contains(r.Status)) Damaged("Unknown review status");
        }
        foreach (var r in data.ReviewLines)
        {
            Text(r.Result, 20); Text(r.Note, int.MaxValue);
            if (!ReviewResults.Contains(r.Result)) Damaged("Unknown review result");
            Json(r.PendingItem, '{');
        }

        // Database unique indexes are case-sensitive; these are the exact values.
        Unique(data.Categories.Select(x => x.Name), "category name");
        Unique(data.AttributeDefinitions.Select(x => x.Key), "attribute key");
        Unique(data.CategoryAttributes.Select(x => $"{x.CategoryId}/{x.AttributeId}"), "category attribute");
        Unique(data.Articles.Select(x => x.Sku), "sku");
        Unique(data.Locations.Select(x => x.Barcode), "location tag");
        // One tag = one thing: an NFC tag is on one article or one location, never both.
        Unique(data.Articles.Select(x => x.Barcode).Concat(data.Locations.Select(x => x.Barcode)), "NFC tag");
        Unique(data.Items.Select(x => x.SerialNumber), "serial number");

        foreach (var r in data.CategoryAttributes)
            if (!categories.Contains(r.CategoryId) || !attributes.Contains(r.AttributeId)) Damaged("Unknown category attribute reference");
        foreach (var r in data.Locations)
            if (r.ParentId is not null && !locations.Contains(r.ParentId)) Damaged("Unknown parent location");
        foreach (var r in data.Articles)
        {
            if (r.CategoryId is not null && !categories.Contains(r.CategoryId)) Damaged("Unknown category");
            if (r.ImagePath is not null && !BackupFormatHelper.IsValidImagePath(r.ImagePath)) Damaged("Invalid image path");
        }
        foreach (var r in data.Items)
        {
            if (!articles.Contains(r.ArticleId)) Damaged("Unknown item article");
            if (r.LocationId is not null && !locations.Contains(r.LocationId)) Damaged("Unknown item location");
        }
        foreach (var r in data.Movements)
        {
            if (!articles.Contains(r.ArticleId) || !locations.Contains(r.LocationId)) Damaged("Unknown movement reference");
            if (r.ItemId is not null && !items.Contains(r.ItemId)) Damaged("Unknown movement item");
        }
        foreach (var r in data.Reviews)
            if (!locations.Contains(r.LocationId)) Damaged("Unknown review location");
        foreach (var r in data.ReviewLines)
        {
            if (!reviews.Contains(r.ReviewId) || !articles.Contains(r.ArticleId) || !locations.Contains(r.LocationId)) Damaged("Unknown review line reference");
            if (r.ItemId is not null && !items.Contains(r.ItemId)) Damaged("Unknown review line item");
            if (r.FoundLocationId is not null && !locations.Contains(r.FoundLocationId)) Damaged("Unknown review line location");
        }
        if (BackupLocationsHelper.Cycle(data.Locations) is not null) Damaged("Location cycle");
    }

    private static HashSet<string> Ids<T>(IReadOnlyList<T> rows, Func<T, string> id, string table)
    {
        var set = new HashSet<string>(rows.Count * 2, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var value = id(row);
            if (!BackupFormatHelper.IsLowerUuid(value)) Damaged($"Invalid id in {table}");
            if (!set.Add(value)) Damaged($"Duplicate id in {table}");
        }
        return set;
    }

    private static void Uuid(string value)
    {
        if (!BackupFormatHelper.IsLowerUuid(value)) Damaged("Invalid id");
    }

    private static void Unique(IEnumerable<string?> values, string what)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in values)
            if (v is not null && !seen.Add(v)) Damaged($"Duplicate {what}");
    }

    /// <summary>Length in UTF-16 units is what the app counts; PostgreSQL counts characters, so this is never looser.</summary>
    private static void Text(string? value, int maxLength)
    {
        if (value is null) return;
        if (value.Length > maxLength) Damaged("Value too long");
        if (value.Contains('\0')) Damaged("Invalid character");
    }

    private static void Json(string? raw, char first)
    {
        if (raw is null) return;
        if (raw.Length == 0 || raw[0] != first) Damaged("Invalid JSON column");
        if (HasNulCharacter(raw)) Damaged("Invalid character");
    }

    /// <summary>
    /// True when a string or a key of the JSON decodes to a NUL character (jsonb refuses <c>\u0000</c>). The text is
    /// decoded, so an escaped backslash followed by "u0000" is not a NUL. Raw JSON text cannot hold a literal NUL.
    /// </summary>
    private static bool HasNulCharacter(string raw)
    {
        if (!raw.Contains("\\u", StringComparison.Ordinal)) return false; // a NUL can only be written as \u0000
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(raw));
            while (reader.Read())
                if (reader is { TokenType: JsonTokenType.String or JsonTokenType.PropertyName, ValueIsEscaped: true }
                    && reader.GetString()!.Contains('\0'))
                    return true;
            return false;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            Damaged("Invalid JSON column"); // lone surrogate escapes, which jsonb refuses as well
            return true;
        }
    }

    private static void Damaged(string what) => throw new BackupException(BackupError.Damaged, what);
}

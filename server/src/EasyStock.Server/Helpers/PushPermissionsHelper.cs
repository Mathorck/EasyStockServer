using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;

namespace EasyStock.Server.Helpers;

/// <summary>
/// What a pushed row does to the server row and what that needs (docs/03-server-spec.md "Roles",
/// docs/04-server-implementation-plan.md 1.2 #23). A row needs every permission of every operation it does.
/// Admin-only operations have no permission of their own: they need <see cref="Role.Admin"/>.
/// </summary>
public static class PushPermissionsHelper
{
    /// <summary>A requirement: a permission, or null for Admin only.</summary>
    public readonly record struct Need(Permission? Permission)
    {
        public static readonly Need AdminOnly = new(null);
        public static implicit operator Need(Permission p) => new(p);
    }

    public static bool IsAllowed(Role role, object incoming, object? existing) =>
        Needs(incoming, existing).All(n => n.Permission is { } p ? Permissions.Can(role, p) : role == Role.Admin);

    public static IEnumerable<Need> Needs(object incoming, object? existing)
    {
        switch (incoming)
        {
            case Category:
                // Members create categories; editing or deleting one is Admin only.
                yield return existing is null ? Permission.Edit : Need.AdminOnly;
                break;
            case AttributeDefinition:
            case CategoryAttribute:
                // Attributes and templates (category_attribute) are Admin only.
                yield return Need.AdminOnly;
                break;
            case Location l:
                yield return Permission.Edit;
                var oldLocation = existing as Location;
                if (l.Barcode != oldLocation?.Barcode) yield return Permission.LinkTag;
                if (oldLocation is not null && l.ParentId != oldLocation.ParentId) yield return Need.AdminOnly;
                if (l.DeletedAt != oldLocation?.DeletedAt) yield return Need.AdminOnly;
                break;
            case Article a:
                yield return Permission.Edit;
                var oldArticle = existing as Article;
                if (a.Barcode != oldArticle?.Barcode) yield return Permission.LinkTag;
                if (a.DeletedAt != oldArticle?.DeletedAt) yield return Need.AdminOnly;
                break;
            case Item i:
                yield return Permission.Edit;
                if (i.DeletedAt != (existing as Item)?.DeletedAt) yield return Permission.Delete;
                break;
            case StockMovement:
                yield return Permission.StockAction;
                break;
            case StockReview:
            case StockReviewLine:
                yield return Permission.RunReview;
                break;
            default:
                yield return Need.AdminOnly;
                break;
        }
    }
}

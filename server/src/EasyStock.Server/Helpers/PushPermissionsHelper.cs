using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;

namespace EasyStock.Server.Helpers;

/// <summary>
/// What a pushed row does to the server row and what that needs (docs/03-server-spec.md "Roles",
/// docs/04-server-implementation-plan.md 1.2 #23). A row needs every permission of every operation it does.
/// Admin-only operations have no permission of their own: they need <see cref="Role.Admin"/>.
/// A soft delete or an undelete (DeletedAt changes) needs <see cref="Permission.Delete"/>, which Members have, like the app.
/// Admin only: editing a category, attribute or template, and changing the location hierarchy.
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
            case Category c:
                // Members create categories and soft-delete them; any other change is Admin only.
                yield return existing is null ? Permission.Edit : PureDelete(c, existing) ? Permission.Delete : Need.AdminOnly;
                break;
            case AttributeDefinition d:
                // Attributes: Members only soft-delete; creating or editing is Admin only.
                yield return existing is not null && PureDelete(d, existing) ? Permission.Delete : Need.AdminOnly;
                break;
            case CategoryAttribute t:
                // Templates (category_attribute): same as attributes.
                yield return existing is not null && PureDelete(t, existing) ? Permission.Delete : Need.AdminOnly;
                break;
            case Location l:
                yield return Permission.Edit;
                var oldLocation = existing as Location;
                if (l.Barcode != oldLocation?.Barcode) yield return Permission.LinkTag;
                if (oldLocation is not null && l.ParentId != oldLocation.ParentId) yield return Need.AdminOnly;
                if (l.DeletedAt != oldLocation?.DeletedAt) yield return Permission.Delete;
                break;
            case Article a:
                yield return Permission.Edit;
                var oldArticle = existing as Article;
                if (a.Barcode != oldArticle?.Barcode) yield return Permission.LinkTag;
                if (a.DeletedAt != oldArticle?.DeletedAt) yield return Permission.Delete;
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

    /// <summary>
    /// True when only DeletedAt (and UpdatedAt) differ from the server row: a soft delete or an undelete and nothing else.
    /// </summary>
    private static bool PureDelete(object incoming, object existing) => (incoming, existing) switch
    {
        (Category n, Category o) => n.DeletedAt != o.DeletedAt && n.Name == o.Name && n.Description == o.Description
            && n.NameTemplate == o.NameTemplate && n.CreatedAt == o.CreatedAt,
        (AttributeDefinition n, AttributeDefinition o) => n.DeletedAt != o.DeletedAt && n.Key == o.Key && n.Label == o.Label
            && n.DataType == o.DataType && n.Unit == o.Unit && n.Options == o.Options && n.CreatedAt == o.CreatedAt,
        (CategoryAttribute n, CategoryAttribute o) => n.DeletedAt != o.DeletedAt && n.CategoryId == o.CategoryId
            && n.AttributeId == o.AttributeId && n.Required == o.Required && n.SortOrder == o.SortOrder && n.CreatedAt == o.CreatedAt,
        _ => false,
    };
}

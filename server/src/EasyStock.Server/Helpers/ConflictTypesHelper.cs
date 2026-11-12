using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Helpers;

public static class ConflictTypesHelper
{
    /// <summary>The stable code, as in the spec and the app (CATEGORY_RENAMED, ...).</summary>
    public static string Code(this ConflictType t) => t switch
    {
        ConflictType.CategoryRenamed => "CATEGORY_RENAMED",
        ConflictType.SkuRenamed => "SKU_RENAMED",
        ConflictType.BarcodeCleared => "BARCODE_CLEARED",
        ConflictType.SerialRenamed => "SERIAL_RENAMED",
        ConflictType.AttributeMerged => "ATTRIBUTE_MERGED",
        ConflictType.AttributeKeyKept => "ATTRIBUTE_KEY_KEPT",
        ConflictType.CategoryAttributeSkipped => "CATEGORY_ATTRIBUTE_SKIPPED",
        ConflictType.LocationParentKept => "LOCATION_PARENT_KEPT",
        ConflictType.MovementKept => "MOVEMENT_KEPT",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };
}

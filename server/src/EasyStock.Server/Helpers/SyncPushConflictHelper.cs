namespace EasyStock.Server.Helpers;

/// <summary><see cref="Models.SyncPushResult.Conflict"/> codes, the same as the app's BackupMerge.</summary>
public static class SyncPushConflictHelper
{
    public const string CategoryRenamed = "CATEGORY_RENAMED";
    public const string SkuRenamed = "SKU_RENAMED";
    public const string BarcodeCleared = "BARCODE_CLEARED";
    public const string SerialRenamed = "SERIAL_RENAMED";
    public const string AttributeMerged = "ATTRIBUTE_MERGED";
    public const string AttributeKeyKept = "ATTRIBUTE_KEY_KEPT";
    public const string CategoryAttributeSkipped = "CATEGORY_ATTRIBUTE_SKIPPED";
    public const string LocationParentKept = "LOCATION_PARENT_KEPT";
    public const string MovementKept = "MOVEMENT_KEPT";
}

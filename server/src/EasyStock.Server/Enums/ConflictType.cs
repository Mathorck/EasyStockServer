using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Enums;

public enum ConflictType
{
    CategoryRenamed,
    SkuRenamed,
    BarcodeCleared,
    SerialRenamed,
    AttributeMerged,
    AttributeKeyKept,
    CategoryAttributeSkipped,
    LocationParentKept,
    MovementKept,
}

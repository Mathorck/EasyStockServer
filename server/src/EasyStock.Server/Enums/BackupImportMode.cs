using EasyStock.Server.DB;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Enums;

public enum BackupImportMode
{
    Merge,
    Replace,
}

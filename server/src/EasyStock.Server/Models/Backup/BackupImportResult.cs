namespace EasyStock.Server.Models.Backup;

/// <summary>Answer of <c>POST /api/v1/admin/backup</c>: counts per table name, photos stored, conflicts resolved by a merge.</summary>
public sealed record BackupImportResult(
    string Mode,
    IReadOnlyDictionary<string, int> Added,
    IReadOnlyDictionary<string, int> Updated,
    int Photos,
    IReadOnlyList<BackupConflictInfo> Conflicts);

namespace EasyStock.Server.Models;

/// <summary>Answer of <c>POST /api/v1/admin/backup</c>: counts per table name, photos stored, conflicts resolved by a merge.</summary>
public sealed record BackupImportResult(
    string Mode,
    IReadOnlyDictionary<string, int> Added,
    IReadOnlyDictionary<string, int> Updated,
    int Photos,
    IReadOnlyList<BackupConflictInfo> Conflicts);

/// <summary>One conflict a merge resolved. <c>Type</c> is a conflict code (CATEGORY_RENAMED, SKU_RENAMED, ...).</summary>
public sealed record BackupConflictInfo(string Type, string Table, string RowId, string? Value, string? Resolved);

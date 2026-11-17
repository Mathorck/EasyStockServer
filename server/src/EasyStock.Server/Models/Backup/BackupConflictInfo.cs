namespace EasyStock.Server.Models.Backup;

/// <summary>One conflict a merge resolved. <c>Type</c> is a conflict code (CATEGORY_RENAMED, SKU_RENAMED, ...).</summary>
public sealed record BackupConflictInfo(string Type, string Table, string RowId, string? Value, string? Resolved);

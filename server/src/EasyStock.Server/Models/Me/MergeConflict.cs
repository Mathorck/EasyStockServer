using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Models.Me;

/// <summary>One conflict resolved during a merge. Value: what was imported, Resolved: what was saved instead.</summary>
public sealed record MergeConflict(ConflictType Type, BackupTable Table, string RowId, string? Value = null, string? Resolved = null);

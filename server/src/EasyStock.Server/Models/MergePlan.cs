using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Models;

/// <summary>
/// What a merge writes. Inserts and Updates hold whole rows (updates never contain movements).
/// ItemLocationFixes: other local items whose cached location changes (movements are the source of truth).
/// Result: the data after the merge.
/// </summary>
public sealed record MergePlan(
    BackupData Inserts,
    BackupData Updates,
    IReadOnlyDictionary<string, string?> ItemLocationFixes,
    IReadOnlyList<MergeConflict> Conflicts,
    BackupData Result,
    HashSet<string> PhotosNeeded)
{
    public bool IsEmpty => Inserts.IsEmpty && Updates.IsEmpty && ItemLocationFixes.Count == 0;
}

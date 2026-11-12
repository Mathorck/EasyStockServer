using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Helpers;

internal static class BackupLocationsHelper
{
    /// <summary>A location id that is its own ancestor, or null.</summary>
    public static string? Cycle(IEnumerable<BackupLocation> locations)
    {
        var parent = locations.ToDictionary(l => l.Id, l => l.ParentId, StringComparer.Ordinal);
        var safe = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in parent.Keys.Order(StringComparer.Ordinal))
        {
            var path = new HashSet<string>(StringComparer.Ordinal);
            var current = start;
            while (current is not null && !safe.Contains(current))
            {
                if (!path.Add(current)) return current;
                current = parent.GetValueOrDefault(current);
            }
            safe.UnionWith(path);
        }
        return null;
    }

    /// <summary>Locations with each parent before its children (foreign key on parent_id). Assumes no cycle.</summary>
    public static List<BackupLocation> ParentsFirst(IReadOnlyList<BackupLocation> locations)
    {
        var byId = locations.ToDictionary(l => l.Id, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BackupLocation>(locations.Count);

        void Visit(BackupLocation location, int depth)
        {
            if (done.Contains(location.Id) || depth > locations.Count) return;
            if (location.ParentId is not null && byId.TryGetValue(location.ParentId, out var parent)) Visit(parent, depth + 1);
            if (done.Add(location.Id)) result.Add(location);
        }

        foreach (var l in locations.OrderBy(l => l.Id, StringComparer.Ordinal)) Visit(l, 0);
        return result;
    }
}

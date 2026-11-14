using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Helpers;

public static class BackupLocationsHelper
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

    /// <summary>
    /// Locations with each parent before its children (foreign key on parent_id). Assumes no cycle (a cycle still ends:
    /// its members come out in some order). Iterative, so a very deep chain of parents cannot overflow the stack.
    /// </summary>
    public static List<BackupLocation> ParentsFirst(IReadOnlyList<BackupLocation> locations)
    {
        var byId = locations.ToDictionary(l => l.Id, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BackupLocation>(locations.Count);
        var chain = new List<BackupLocation>();
        var inChain = new HashSet<string>(StringComparer.Ordinal);

        foreach (var l in locations.OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            if (done.Contains(l.Id)) continue;
            // Climb to the first ancestor already written (or the root), then write the chain from the top down.
            chain.Clear();
            inChain.Clear();
            var current = l;
            while (true)
            {
                chain.Add(current);
                inChain.Add(current.Id);
                if (current.ParentId is null || !byId.TryGetValue(current.ParentId, out var parent)
                    || done.Contains(parent.Id) || inChain.Contains(parent.Id)) break;
                current = parent;
            }
            for (var i = chain.Count - 1; i >= 0; i--)
                if (done.Add(chain[i].Id)) result.Add(chain[i]);
        }
        return result;
    }
}

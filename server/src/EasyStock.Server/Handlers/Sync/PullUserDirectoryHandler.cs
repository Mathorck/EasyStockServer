using EasyStock.Server.DB;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Sync;

/// <summary>
/// The <c>users</c> of a pull, read from the <c>users</c> table through the request's DbContext (so inside the
/// pull's snapshot). A deleted user is named "Deleted user".
/// </summary>
public sealed class PullUserDirectoryHandler(EasyStockDbContext db) : IPullUserDirectoryHandler
{
    public const string DeletedUserName = "Deleted user";

    public async Task<IReadOnlyList<PullUserChange>> ChangedSinceAsync(long since, int take, CancellationToken ct)
    {
        var rows = await db.Users.AsNoTracking()
            .Where(u => u.ServerSeq > since)
            .OrderBy(u => u.ServerSeq)
            .Take(take)
            .Select(u => new { u.Id, u.DisplayName, u.DeletedAt, u.ServerSeq })
            .ToListAsync(ct);
        return rows.Select(u => new PullUserChange(u.Id, Name(u.DisplayName, u.DeletedAt), u.ServerSeq)).ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, string DisplayName)>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.DeletedAt })
            .ToListAsync(ct);
        return rows.Select(u => (u.Id, Name(u.DisplayName, u.DeletedAt))).ToList();
    }

    private static string Name(string displayName, DateTimeOffset? deletedAt) => deletedAt is null ? displayName : DeletedUserName;
}

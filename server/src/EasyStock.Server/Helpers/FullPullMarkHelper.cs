using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Helpers;

/// <summary>
/// Sessions that must pull everything again from <c>since=0</c>: a Guest promoted to a full account holds a
/// Guest-filtered copy (movements without author, device and comment, no reviews), and the cursor it holds is past
/// the rows it never received. The global dataset epoch stays as it is (other users are not affected): an
/// incremental pull of a marked session gets 409 <c>dataset-reset</c> with the unchanged epoch, so the app wipes
/// its server rows and pulls from 0. The mark is a row of the server-only <c>setting</c> table, keyed by session,
/// and is removed by the first full pull made as a full account.
/// </summary>
public static class FullPullMarkHelper
{
    private const string KeyPrefix = "full_pull_required:";

    private static string KeyOf(Guid sessionId) => KeyPrefix + sessionId.ToString("D");

    /// <summary>Marks every open session of the user, in the caller's transaction.</summary>
    public static async Task MarkUserSessionsAsync(EasyStockDbContext db, Guid userId, CancellationToken ct)
    {
        var sessionIds = await db.DeviceSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .Select(s => s.Id).ToListAsync(ct);
        foreach (var id in sessionIds)
        {
            var key = KeyOf(id);
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO setting (key, value) VALUES ({key}, 'true'::jsonb) ON CONFLICT (key) DO NOTHING", ct);
        }
    }

    public static Task<bool> IsMarkedAsync(EasyStockDbContext db, Guid sessionId, CancellationToken ct)
    {
        var key = KeyOf(sessionId);
        return db.Set<Setting>().AsNoTracking().AnyAsync(s => s.Key == key, ct);
    }

    /// <summary>Removes the mark (no-op when there is none).</summary>
    public static Task ClearAsync(EasyStockDbContext db, Guid sessionId, CancellationToken ct)
    {
        var key = KeyOf(sessionId);
        return db.Set<Setting>().Where(s => s.Key == key).ExecuteDeleteAsync(ct);
    }
}

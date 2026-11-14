using EasyStock.Server.Models;

namespace EasyStock.Server.Interfaces;

/// <summary>
/// The <c>users</c> part of a pull (docs/04-server-implementation-plan.md, 1.2 #11 and 3.1).
/// Implementations must read through the request's scoped <c>EasyStockDbContext</c>: <see cref="Handlers.Sync.PullHandler"/>
/// calls them inside its REPEATABLE READ transaction, so they see the same snapshot as the shared rows.
/// </summary>
public interface IPullUserDirectoryHandler
{
    /// <summary>
    /// Users with <c>server_seq &gt; since</c> who authored at least one movement, in <c>server_seq</c> order, at most
    /// <paramref name="take"/>. Users no movement names are never sent (privacy).
    /// They take part in the page cut like the shared tables. A deleted user carries its "Deleted user" name.
    /// </summary>
    Task<IReadOnlyList<PullUserChange>> ChangedSinceAsync(long since, int take, CancellationToken ct);

    /// <summary>Display names of <paramref name="ids"/> (authors of the returned movements); unknown ids are left out.</summary>
    Task<IReadOnlyList<(Guid Id, string DisplayName)>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

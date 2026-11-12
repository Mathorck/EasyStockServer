using System.Text.Json.Nodes;

namespace EasyStock.Server.Models;

/// <summary>
/// GET /api/v1/sync/pull answer (docs/03-server-spec.md, "Sync"). <see cref="Rows"/>: wire rows by snake_case
/// table name, each list in <c>server_seq</c> order; a table without rows in the page is absent.
/// </summary>
public sealed record SyncPullResponse(
    IReadOnlyDictionary<string, List<JsonObject>> Rows,
    IReadOnlyList<SyncPullUser> Users,
    IReadOnlyList<SyncPullAlert> Alerts,
    long Next,
    bool HasMore,
    string? DatasetEpoch);

/// <summary>Display name of a user referenced by the pulled movements, or changed in the window.</summary>
public sealed record SyncPullUser(Guid Id, string DisplayName);

/// <summary>A low-stock alert for the caller. <see cref="CrossedAt"/>: epoch ms; <see cref="Stock"/>: thousandths.</summary>
public sealed record SyncPullAlert(Guid Id, Guid ArticleId, long CrossedAt, long Stock);

using System.Text.Json.Nodes;

namespace EasyStock.Server.Models.Sync;

/// <summary>
/// GET /api/v1/sync/pull answer (docs/03-server-spec.md, "Sync"). <see cref="Rows"/>: wire rows by snake_case
/// table name, each list in <c>server_seq</c> order; a table without rows in the page is absent.
/// <see cref="DatasetEpoch"/>: the dataset epoch the rows belong to.
/// </summary>
public sealed record SyncPullResponse(
    IReadOnlyDictionary<string, List<JsonObject>> Rows,
    IReadOnlyList<SyncPullUser> Users,
    IReadOnlyList<SyncPullAlert> Alerts,
    long Next,
    bool HasMore,
    string DatasetEpoch);

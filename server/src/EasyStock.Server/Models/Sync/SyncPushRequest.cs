using System.Text.Json.Nodes;

namespace EasyStock.Server.Models.Sync;

/// <summary>
/// <c>POST /api/v1/sync/push</c> body. <see cref="Rows"/>: wire rows by table name (snake_case keys), each row
/// with camelCase column names and epoch-millisecond times, as the app sends them. A row that edits a server row
/// (soft delete included) also carries <c>baseSeq</c>: the <c>serverSeq</c> of the server version it was based on.
/// If the server row changed since, the row is rejected with reason <c>conflict</c> (first change wins).
/// </summary>
public sealed record SyncPushRequest(string? BatchId, Dictionary<string, List<JsonNode?>>? Rows);

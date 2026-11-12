using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EasyStock.Server.Models;

/// <summary>
/// <c>POST /api/v1/sync/push</c> body. <see cref="Rows"/>: wire rows by table name (snake_case keys), each row
/// with camelCase column names and epoch-millisecond times, as the app sends them. A row that edits a server row
/// (soft delete included) also carries <c>baseSeq</c>: the <c>serverSeq</c> of the server version it was based on.
/// If the server row changed since, the row is rejected with reason <c>conflict</c> (first change wins).
/// </summary>
public sealed record SyncPushRequest(string? BatchId, Dictionary<string, List<JsonNode?>>? Rows);

/// <summary>The answer: one result per pushed row, in the order the server applied them.</summary>
public sealed record SyncPushResponse(IReadOnlyList<SyncPushResult> Results);

/// <summary>
/// The answer for one row. <see cref="Row"/>: the fields the server changed (<c>changed</c>; the whole server row
/// for <c>ATTRIBUTE_MERGED</c> and <c>MOVEMENT_KEPT</c>), or its current version (<c>rejected</c>, empty when it
/// has none). <see cref="ServerSeq"/>: the row's <c>server_seq</c> on the server after this push (the server row's
/// for a rejected row); the phone sends it back as <c>baseSeq</c> on its next edit of the row. Absent when the
/// server has no such row.
/// </summary>
public sealed record SyncPushResult(
    string Table,
    string Id,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Conflict = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? Row = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ServerSeq = null);

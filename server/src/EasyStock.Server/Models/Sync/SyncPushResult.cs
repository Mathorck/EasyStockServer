using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EasyStock.Server.Models.Sync;

/// <summary>
/// The answer for one row. <see cref="Row"/>: the author fields (<c>id</c>, <c>createdBy</c>, <c>deviceId</c>) of an accepted movement, set by the
/// server, the fields the server changed (<c>changed</c>; the whole server row
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

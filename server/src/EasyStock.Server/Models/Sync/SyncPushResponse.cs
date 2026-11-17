
namespace EasyStock.Server.Models.Sync;

/// <summary>The answer: one result per pushed row, in the order the server applied them.</summary>
public sealed record SyncPushResponse(IReadOnlyList<SyncPushResult> Results);

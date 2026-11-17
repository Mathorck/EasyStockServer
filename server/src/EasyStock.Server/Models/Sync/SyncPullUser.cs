
namespace EasyStock.Server.Models.Sync;

/// <summary>Display name of a user referenced by the pulled movements, or changed in the window.</summary>
public sealed record SyncPullUser(Guid Id, string DisplayName);

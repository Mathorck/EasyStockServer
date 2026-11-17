
namespace EasyStock.Server.Models.Sync;

/// <summary>A low-stock alert for the caller. <see cref="CrossedAt"/>: epoch ms; <see cref="Stock"/>: thousandths.</summary>
public sealed record SyncPullAlert(Guid Id, Guid ArticleId, long CrossedAt, long Stock);

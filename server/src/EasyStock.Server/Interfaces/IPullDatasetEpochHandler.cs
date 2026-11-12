namespace EasyStock.Server.Interfaces;

/// <summary>
/// The server's <c>dataset_epoch</c> setting (plan 1.2 #21), sent as <c>datasetEpoch</c> in every pull answer.
/// It must be a stable value: the app wipes its server data when the epoch it holds differs from the answered one.
/// Read inside the pull transaction (same scoped DbContext).
/// </summary>
public interface IPullDatasetEpochHandler
{
    /// <summary>Null while the server has no epoch (the app then keeps its data).</summary>
    Task<string?> GetAsync(CancellationToken ct);
}

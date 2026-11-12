using EasyStock.Server.Interfaces;

namespace EasyStock.Server.Handlers.Sync;

/// <summary>Placeholder until the settings table exists: no epoch.</summary>
internal sealed class NullPullDatasetEpochHandler : IPullDatasetEpochHandler
{
    public Task<string?> GetAsync(CancellationToken ct) => Task.FromResult<string?>(null);
}

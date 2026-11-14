using EasyStock.Server.Handlers.Images;

namespace EasyStock.Server.Helpers;

public static class ImagesHelper
{
    /// <summary>
    /// Transaction-level advisory lock around the quota check and the <c>image</c> row insert or delete, so concurrent
    /// uploads, imports and the orphan purge cannot go past the quota or leave a row without its file. Lock order:
    /// the sync lock first (import, cleanup), then this one.
    /// </summary>
    public const long LockKey = 0x4553_494D_4147; // "ESIMAG"

    /// <summary>A photo no article references is deleted once it has been stored this long (spec: 30 days).</summary>
    public static readonly TimeSpan OrphanGrace = TimeSpan.FromDays(30);

    public static IServiceCollection AddImages(this IServiceCollection services) =>
        services.AddSingleton<ImageHandler>();
}

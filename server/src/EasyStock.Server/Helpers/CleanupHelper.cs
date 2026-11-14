using EasyStock.Server.Handlers.Cleanup;

namespace EasyStock.Server.Helpers;

/// <summary>Retention rules and wiring of the background cleanup job (docs/03-server-spec.md, "Background jobs").</summary>
public static class CleanupHelper
{
    /// <summary>First run after the start, so a restart loop does not hammer the database.</summary>
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);

    /// <summary>Invites, resets and promotions are deleted this long after they expired.</summary>
    public static readonly TimeSpan ExpiredGrace = TimeSpan.FromDays(30);

    /// <summary>A revoked session stays visible to Admins (device list) this long, then is deleted.</summary>
    public static readonly TimeSpan RevokedSessionGrace = TimeSpan.FromDays(30);

    /// <summary>Transaction-level advisory lock: only one instance cleans at a time, the others skip the run.</summary>
    public const long LockKey = 0x4553_434C_4E55; // "ESCLNU"

    public static IServiceCollection AddCleanup(this IServiceCollection services, bool job = true)
    {
        services.AddScoped<CleanupHandler>();
        if (job)
            services.AddHostedService<CleanupService>();
        return services;
    }
}

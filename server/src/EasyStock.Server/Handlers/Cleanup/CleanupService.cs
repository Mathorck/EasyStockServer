using EasyStock.Server.Configuration;
using EasyStock.Server.Helpers;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Handlers.Cleanup;

/// <summary>
/// Background job: first run one minute after the start, then every <c>EASYSTOCK_CLEANUP_INTERVAL_MINUTES</c>
/// (default 60). Each run is a DI scope of its own; a failure is logged and the next tick tries again.
/// </summary>
public sealed partial class CleanupService(
    IServiceScopeFactory scopes,
    IOptions<ServerOptions> options,
    TimeProvider time,
    ILogger<CleanupService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(CleanupHelper.FirstRunDelay, time, ct);
            await RunOnceAsync(ct);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes), time);
            while (await timer.WaitForNextTickAsync(ct))
                await RunOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>One pass. Never throws (except to cancel).</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var counts = await scope.ServiceProvider.GetRequiredService<CleanupHandler>().RunOnceAsync(ct);
            if (counts is null) LogSkipped(log);
            else LogDone(log, counts.PromotionsExpired, counts.Invites, counts.PasswordResets, counts.Promotions, counts.Sessions);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogFailed(log, e.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Cleanup: {PromotionsExpired} promotions expired, deleted {Invites} invites, {PasswordResets} resets, {Promotions} promotions, {Sessions} sessions")]
    private static partial void LogDone(ILogger log, int promotionsExpired, int invites, int passwordResets, int promotions, int sessions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleanup skipped: another instance is running it")]
    private static partial void LogSkipped(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleanup failed ({ErrorType}); retrying at the next interval")]
    private static partial void LogFailed(ILogger log, string errorType);
}

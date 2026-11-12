using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// Runs after <see cref="DatabaseMigrator"/>: loads (or creates) the signing key, so a broken data directory
/// fails the start, then prints the setup token if needed. Not registered for the OpenAPI generator.
/// </summary>
internal sealed class AuthStartupHandler(SigningKeyHandler keys, IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        _ = keys.Key;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SetupHandler>().PrepareAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

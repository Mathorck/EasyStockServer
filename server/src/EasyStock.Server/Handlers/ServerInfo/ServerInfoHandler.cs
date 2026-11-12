using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Handlers.Sync;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.ServerInfo;

/// <summary>
/// The public <c>GET /server-info</c> answer: setup state, whether the server holds data, whether Guest signup works,
/// and the dataset epoch.
/// </summary>
public sealed class ServerInfoHandler(EasyStockDbContext db, SetupHandler setup, DatasetEpochHandler datasetEpoch)
{
    public async Task<Models.ServerInfo> GetAsync(CancellationToken ct) => new(
        ServerName: "EasyStock",
        ServerVersion: Compat.ServerVersion,
        ApiVersion: Compat.ApiVersion,
        MinAppVersion: Compat.MinAppVersion,
        SupportedSchemaVersions: Compat.SupportedSchemaVersions,
        SetupDone: await setup.IsDoneAsync(ct),
        HasData: await HasDataAsync(ct),
        GuestSignupEnabled: await GuestSignupWorksAsync(ct),
        DatasetEpoch: await datasetEpoch.GetAsync(ct));

    /// <summary>A Guest can sign up only when the printed QR code is on and a token has been generated.</summary>
    private Task<bool> GuestSignupWorksAsync(CancellationToken ct) =>
        db.PublicInvites.AsNoTracking().AnyAsync(p => p.Enabled && p.TokenHash != null, ct);

    /// <summary>Any row in one of the 9 shared tables (one cheap EXISTS each; deleted rows count: they are still data).</summary>
    private async Task<bool> HasDataAsync(CancellationToken ct) =>
        await db.Categories.AnyAsync(ct)
        || await db.AttributeDefinitions.AnyAsync(ct)
        || await db.CategoryAttributes.AnyAsync(ct)
        || await db.Articles.AnyAsync(ct)
        || await db.Locations.AnyAsync(ct)
        || await db.Items.AnyAsync(ct)
        || await db.StockMovements.AnyAsync(ct)
        || await db.StockReviews.AnyAsync(ct)
        || await db.StockReviewLines.AnyAsync(ct);
}

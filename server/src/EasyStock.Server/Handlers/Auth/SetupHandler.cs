using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.DB;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// First start: until the first Admin exists, each start makes a new setup token, keeps its hash and prints it
/// with a QR code. That log line is the one deliberate exception to "logs never contain tokens".
/// Setup counts as done once <c>setup_state.done</c> is set or any user exists.
/// </summary>
public sealed partial class SetupHandler(
    EasyStockDbContext db,
    UserManager<AppUser> users,
    SessionHandler sessions,
    QrContentHandler qr,
    TimeProvider time,
    ILogger<SetupHandler> log)
{
    public async Task<bool> IsDoneAsync(CancellationToken ct) =>
        await db.SetupStates.AnyAsync(s => s.Done, ct) || await db.Users.AnyAsync(ct);

    /// <summary>Startup step: a new setup token if setup is not done.</summary>
    public async Task PrepareAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AuthLimitsHelper.SetupLockKey, ct);
        var state = await db.SetupStates.SingleOrDefaultAsync(s => s.Id == SetupState.SingletonId, ct);
        if (state is null)
        {
            state = new SetupState();
            db.SetupStates.Add(state);
        }
        if (state.Done || await db.Users.AnyAsync(ct))
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        var token = SecretTokensHelper.New();
        state.TokenHash = SecretTokensHelper.Hash(token);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var link = qr.Build(QrContentHandler.Setup, token);
        LogSetupToken(log, token, link, Environment.NewLine + QrContentHandler.Ascii(link));
    }

    /// <summary><c>POST /auth/setup</c>: creates the first Admin, once.</summary>
    public async Task<TokenAnswer> CompleteAsync(SetupRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AuthLimitsHelper.SetupLockKey, ct);
        var state = await db.SetupStates.SingleOrDefaultAsync(s => s.Id == SetupState.SingletonId, ct);
        if (state is null || state.Done || await db.Users.AnyAsync(ct))
            throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypes.SetupDone);
        if (!SecretTokensHelper.Matches(request.SetupToken, state.TokenHash))
            throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypes.InviteInvalid);

        var user = await AccountsHelper.NewUserAsync(db, time, request.DisplayName, Role.Admin, UserKind.Full, ct);
        user.UserName = request.Username;
        AccountsHelper.ThrowIfFailed(await users.CreateAsync(user, request.Password));

        state.Done = true;
        state.DoneAt = time.GetUtcNow();
        state.TokenHash = null;
        var answer = await sessions.CreateAsync(user, request.DeviceName, ct);
        await tx.CommitAsync(ct);
        LogSetupDone(log, user.Id);
        return answer;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "EasyStock is not set up yet. Setup token: {SetupToken} (link {SetupLink}). Scan this QR code with the EasyStock app:{SetupQr}")]
    private static partial void LogSetupToken(ILogger log, string setupToken, string setupLink, string setupQr);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setup done: user {UserId} is the first Admin")]
    private static partial void LogSetupDone(ILogger log, Guid userId);
}

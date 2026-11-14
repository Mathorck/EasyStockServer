using System.Security.Cryptography;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// Device sessions and their refresh tokens (docs/04-server-implementation-plan.md, Phase 3). Each refresh
/// rotates the token and keeps the old hash as "previous". Presenting any replaced token of the session means it
/// was copied, so the session is revoked (<c>token-reuse</c>), except the just-replaced one within
/// <see cref="AuthLimitsHelper.RefreshReuseGrace"/>, which answers with the current pair again (parallel refreshes,
/// retry after a lost answer).
/// </summary>
public sealed partial class SessionHandler(
    EasyStockDbContext db,
    AccessTokenHandler tokens,
    RefreshTokenHandler refreshTokens,
    TimeProvider time,
    ILogger<SessionHandler> log)
{
    /// <summary>Opens a session for <paramref name="user"/> (saved with any other pending change).</summary>
    public async Task<TokenAnswer> CreateAsync(AppUser user, string deviceName, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var refresh = refreshTokens.New(id);
        var session = new DeviceSession
        {
            Id = id,
            UserId = user.Id,
            DeviceName = AccountsHelper.CleanDeviceName(deviceName),
            RefreshTokenHash = SecretTokensHelper.Hash(refresh),
            CreatedAt = now,
            LastUsedAt = now,
        };
        db.DeviceSessions.Add(session);
        await db.SaveChangesAsync(ct);
        LogCreated(log, session.Id, user.Id);
        return Answer(user, session.Id, refresh);
    }

    public TokenAnswer Answer(AppUser user, Guid sessionId, string refreshToken)
    {
        var (access, expires) = tokens.CreateAccessToken(user, sessionId);
        return new TokenAnswer(access, refreshToken, expires, user.ToSessionUser());
    }

    /// <summary><c>POST /auth/refresh</c>.</summary>
    public async Task<TokenAnswer> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = SecretTokensHelper.Hash(refreshToken);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Row lock: two refreshes of one session run one after the other, so the second sees a "previous" token.
        // A token that names its session is looked up by id, so an older token of the session is found too.
        var session = (refreshTokens.TryReadSession(refreshToken, out var sessionId)
            ? await db.DeviceSessions
                .FromSql($"SELECT * FROM device_session WHERE id = {sessionId} FOR UPDATE")
                .ToListAsync(ct)
            : await db.DeviceSessions
                .FromSql($"SELECT * FROM device_session WHERE refresh_token_hash = {hash} OR previous_token_hash = {hash} FOR UPDATE")
                .ToListAsync(ct)).FirstOrDefault();

        if (session is null || session.RevokedAt is not null)
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.SessionRevoked);

        if (!CryptographicOperations.FixedTimeEquals(session.RefreshTokenHash, hash))
        {
            if (session.PreviousTokenHash is { } previous && CryptographicOperations.FixedTimeEquals(previous, hash)
                && session.RotatedAt is { } rotatedAt && now - rotatedAt <= AuthLimitsHelper.RefreshReuseGrace)
                return await RepeatAsync(session, hash, ct);

            Revoke(session, AuthLimitsHelper.RevokeReasons.TokenReuse, now);
            await db.SaveChangesAsync(ct);
            await DeletePushRegistrationsAsync(session.Id, ct);
            await tx.CommitAsync(ct);
            LogReuse(log, session.Id, session.UserId);
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.SessionRevoked);
        }

        if (session.LastUsedAt + AuthLimitsHelper.RefreshIdleLifetime < now)
        {
            Revoke(session, AuthLimitsHelper.RevokeReasons.Expired, now);
            await db.SaveChangesAsync(ct);
            await DeletePushRegistrationsAsync(session.Id, ct);
            await tx.CommitAsync(ct);
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.SessionRevoked);
        }

        var user = await ActiveUserAsync(session.UserId, ct);
        var next = Rotate(session, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Answer(user, session.Id, next);
    }

    /// <summary>
    /// Gives the session a new refresh token, inside the caller's transaction (complete-promotion). The old token
    /// becomes "previous", as after a refresh.
    /// </summary>
    public async Task<string> RotateAsync(Guid sessionId, CancellationToken ct)
    {
        var session = (await db.DeviceSessions
            .FromSql($"SELECT * FROM device_session WHERE id = {sessionId} FOR UPDATE")
            .ToListAsync(ct)).FirstOrDefault();
        if (session is null || session.RevokedAt is not null)
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.SessionRevoked);
        var next = Rotate(session, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return next;
    }

    /// <summary>
    /// The just-replaced token came back within the grace window: answer with the current token again, without
    /// rotating. The current token is derived from the replaced one, so it is rebuilt rather than stored.
    /// </summary>
    private async Task<TokenAnswer> RepeatAsync(DeviceSession session, byte[] presentedHash, CancellationToken ct)
    {
        var current = refreshTokens.Next(session.Id, presentedHash);
        // Fails only when the signing key changed since the rotation: refuse without revoking.
        if (!SecretTokensHelper.Matches(current, session.RefreshTokenHash))
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.Unauthorized);
        var user = await ActiveUserAsync(session.UserId, ct);
        LogRepeated(log, session.Id);
        return Answer(user, session.Id, current);
    }

    private async Task<AppUser> ActiveUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (user.DisabledAt is not null || user.DeletedAt is not null)
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.AccountDisabled);
        return user;
    }

    public async Task<bool> RevokeAsync(Guid sessionId, string reason, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var n = await db.DeviceSessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, reason), ct);
        await DeletePushRegistrationsAsync(sessionId, ct);
        return n > 0;
    }

    /// <summary>Every session, revoked ones included, most recently used first (Admin: <c>GET /admin/devices</c>).</summary>
    public async Task<IReadOnlyList<AdminDeviceAnswer>> ListAllAsync(CancellationToken ct) =>
        await db.DeviceSessions.AsNoTracking()
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new AdminDeviceAnswer(
                s.Id, s.UserId, s.User.DisplayName, s.DeviceName, s.CreatedAt, s.LastUsedAt, s.RevokedAt, s.RevokeReason))
            .ToListAsync(ct);

    /// <summary>
    /// Admin revoke (<c>DELETE /admin/devices/{id}</c>). Idempotent: a session already revoked keeps its first
    /// reason and time. 404 not-found when no such session exists.
    /// </summary>
    public async Task RevokeByAdminAsync(Guid sessionId, CancellationToken ct)
    {
        if (await RevokeAsync(sessionId, AuthLimitsHelper.RevokeReasons.Admin, ct))
        {
            LogAdminRevoked(log, sessionId);
            return;
        }
        if (!await db.DeviceSessions.AnyAsync(s => s.Id == sessionId, ct))
            throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypesHelper.NotFound);
    }

    /// <summary>Revokes every live session of the user, except <paramref name="keep"/>.</summary>
    public async Task<int> RevokeAllAsync(Guid userId, string reason, Guid? keep, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var revoked = await db.DeviceSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != keep)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, reason), ct);
        // The cascade from device_session only fires on delete, and a revoked session is kept: drop its endpoint here.
        await db.Set<PushRegistration>()
            .Where(p => db.DeviceSessions.Any(s => s.Id == p.DeviceSessionId && s.UserId == userId && s.RevokedAt != null))
            .ExecuteDeleteAsync(ct);
        return revoked;
    }

    /// <summary>A revoked session keeps its row, so its push endpoint must go explicitly (the FK cascade fires on delete only).</summary>
    private Task<int> DeletePushRegistrationsAsync(Guid sessionId, CancellationToken ct) =>
        db.Set<PushRegistration>().Where(p => p.DeviceSessionId == sessionId).ExecuteDeleteAsync(ct);

    private string Rotate(DeviceSession session, DateTimeOffset now)
    {
        var next = refreshTokens.Next(session.Id, session.RefreshTokenHash);
        session.PreviousTokenHash = session.RefreshTokenHash;
        session.RefreshTokenHash = SecretTokensHelper.Hash(next);
        session.RotatedAt = now;
        session.LastUsedAt = now;
        return next;
    }

    private static void Revoke(DeviceSession session, string reason, DateTimeOffset now)
    {
        session.RevokedAt = now;
        session.RevokeReason = reason;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} opened for user {UserId}")]
    private static partial void LogCreated(ILogger log, Guid sessionId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} revoked by an Admin")]
    private static partial void LogAdminRevoked(ILogger log, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reused: session {SessionId} of user {UserId} revoked")]
    private static partial void LogReuse(ILogger log, Guid sessionId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId}: replaced refresh token presented again within the grace window")]
    private static partial void LogRepeated(ILogger log, Guid sessionId);
}

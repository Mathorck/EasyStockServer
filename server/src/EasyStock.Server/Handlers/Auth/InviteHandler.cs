using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>Single-use invitations for full accounts (7 days). The Admin endpoints call Create, List and Cancel.</summary>
public sealed partial class InviteHandler(
    EasyStockDbContext db,
    UserManager<AppUser> users,
    SessionHandler sessions,
    QrContentHandler qr,
    TimeProvider time,
    ILogger<InviteHandler> log)
{
    /// <summary>Invites are for full accounts only: Guests come from the public QR code.</summary>
    public static bool IsInvitableRole(Role role) => role is Role.Admin or Role.Member or Role.Viewer;

    public async Task<InviteAnswer> CreateAsync(Role role, Guid? createdBy, CancellationToken ct)
    {
        if (!IsInvitableRole(role))
            throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypes.RoleNotAllowed);
        var now = time.GetUtcNow();
        var token = SecretTokensHelper.New();
        var invite = new Invite
        {
            Id = Guid.CreateVersion7(now),
            TokenHash = SecretTokensHelper.Hash(token),
            Role = role,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = now + AuthLimitsHelper.InviteLifetime,
        };
        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);
        LogCreated(log, invite.Id, role.ToWire());
        return ToAnswer(invite) with { Token = token, Qr = qr.Build(QrContentHandler.Invite, token) };
    }

    /// <summary>Invites not used and not expired, oldest first.</summary>
    public async Task<IReadOnlyList<InviteAnswer>> ListOpenAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var open = await db.Invites.AsNoTracking()
            .Where(i => i.UsedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
        return open.Select(ToAnswer).ToList();
    }

    /// <summary>Deletes an unused invite; 404 not-found if there is none.</summary>
    public async Task CancelAsync(Guid inviteId, CancellationToken ct)
    {
        var n = await db.Invites.Where(i => i.Id == inviteId && i.UsedAt == null).ExecuteDeleteAsync(ct);
        if (n == 0) throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypes.NotFound);
    }

    /// <summary><c>POST /auth/invite/redeem</c>.</summary>
    public async Task<TokenAnswer> RedeemAsync(RedeemInviteRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = SecretTokensHelper.Hash(request.InviteToken);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invite = await db.Invites.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == hash, ct)
            ?? throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypes.InviteInvalid);
        if (invite.UsedAt is not null) throw Gone(ProblemTypes.InviteUsed);
        if (invite.ExpiresAt <= now) throw Gone(ProblemTypes.InviteExpired);

        // Conditional update: of two redeems at once, only one gets the row.
        var claimed = await db.Invites.Where(i => i.Id == invite.Id && i.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), ct);
        if (claimed == 0) throw Gone(ProblemTypes.InviteUsed);

        var user = await AccountsHelper.NewUserAsync(db, time, request.DisplayName, invite.Role, UserKind.Full, ct);
        user.UserName = request.Username;
        AccountsHelper.ThrowIfFailed(await users.CreateAsync(user, request.Password));
        var answer = await sessions.CreateAsync(user, request.DeviceName, ct);
        await tx.CommitAsync(ct);
        LogRedeemed(log, invite.Id, user.Id);
        return answer;
    }

    private static InviteAnswer ToAnswer(Invite i) => new(i.Id, i.Role.ToWire(), i.CreatedAt, i.ExpiresAt);

    private static ProblemException Gone(string type) => ProblemException.Of(StatusCodes.Status410Gone, type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invite {InviteId} created for role {Role}")]
    private static partial void LogCreated(ILogger log, Guid inviteId, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invite {InviteId} redeemed by new user {UserId}")]
    private static partial void LogRedeemed(ILogger log, Guid inviteId, Guid userId);
}

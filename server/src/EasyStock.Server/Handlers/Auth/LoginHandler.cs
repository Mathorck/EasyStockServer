using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Identity;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// <c>POST /auth/login</c>. The failed-attempt limit (5 failures, 15 minutes, per account and client address) is kept
/// by <see cref="PasswordAttemptHandler"/>. An unknown user, a wrong password and a locked account all answer the same
/// 401 invalid-credentials, so the answer never reveals whether an account exists. "Disabled" is only told after a
/// correct password, for the same reason.
/// </summary>
public sealed partial class LoginHandler(
    UserManager<AppUser> users, PasswordAttemptHandler attempts, SessionHandler sessions, ILogger<LoginHandler> log)
{
    public async Task<TokenAnswer> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByNameAsync(request.Username);
        if (user is null || user.Kind != UserKind.Full || user.PasswordHash is null)
        {
            // Same work as a real check, so the timing does not tell which usernames exist.
            attempts.VerifyDummy(request.Password);
            LogUnknown(log);
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.InvalidCredentials);
        }

        switch (await attempts.CheckAsync(user, request.Password, ct))
        {
            case PasswordAttempt.Right:
                break;
            case PasswordAttempt.WrongAndLocked:
                LogLocked(log, user.Id);
                throw AccountsHelper.Unauthenticated(ProblemTypesHelper.InvalidCredentials);
            case PasswordAttempt.Locked:
                LogWhileLocked(log, user.Id);
                throw AccountsHelper.Unauthenticated(ProblemTypesHelper.InvalidCredentials);
            default:
                LogFailed(log, user.Id);
                throw AccountsHelper.Unauthenticated(ProblemTypesHelper.InvalidCredentials);
        }

        if (user.DisabledAt is not null || user.DeletedAt is not null)
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.AccountDisabled);

        var answer = await sessions.CreateAsync(user, request.DeviceName, ct);
        LogLoggedIn(log, user.Id);
        return answer;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged in")]
    private static partial void LogLoggedIn(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Failed login for user {UserId}")]
    private static partial void LogFailed(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} locked out after too many failed logins")]
    private static partial void LogLocked(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login refused for user {UserId}: account locked")]
    private static partial void LogWhileLocked(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Failed login for an unknown username")]
    private static partial void LogUnknown(ILogger log);
}

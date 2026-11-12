using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Helpers;

/// <summary>
/// Problem Details <c>type</c> values (docs/04-server-implementation-plan.md, 3.6). They are bare slugs, the same
/// strings the app matches on; the HTTP status that goes with each one is listed in the plan.
/// </summary>
public static class ProblemTypesHelper
{
    public const string ValidationFailed = "validation-failed";        // 400
    public const string ShortCodeMismatch = "short-code-mismatch";     // 400
    public const string HashMismatch = "hash-mismatch";                // 400
    public const string NotJpeg = "not-a-jpeg";                        // 400
    public const string InvalidCredentials = "invalid-credentials";    // 401 (400 on POST /me/password)
    public const string AccountLocked = "account-locked";              // 401, with retryAfter
    public const string AccountDisabled = "account-disabled";          // 401
    public const string Unauthorized = "unauthorized";                 // 401
    public const string TokenExpired = "token-expired";                // 401
    public const string SessionRevoked = "session-revoked";            // 401
    public const string NotAllowed = "not-allowed";                    // 403
    public const string GuestSignupOff = "guest-signup-off";           // 403
    public const string NotFound = "not-found";                        // 404
    public const string InviteInvalid = "invite-invalid";              // 404
    public const string NoPromotion = "no-promotion";                  // 404
    public const string SetupDone = "setup-done";                      // 409
    public const string UsernameTaken = "username-taken";              // 409
    public const string LastAdmin = "last-admin";                      // 409
    public const string RoleNotAllowed = "role-not-allowed";           // 409
    public const string UsePromotion = "use-promotion";                // 409
    public const string GuestLimitReached = "guest-limit-reached";     // 409
    public const string BatchIdReused = "batch-id-reused";             // 409
    public const string DatasetReset = "dataset-reset";                // 409
    public const string InviteExpired = "invite-expired";              // 410
    public const string InviteUsed = "invite-used";                    // 410
    public const string ResetExpired = "reset-expired";                // 410
    public const string ResetUsed = "reset-used";                      // 410
    public const string PromotionExpired = "promotion-expired";        // 410
    public const string ImageTooLarge = "image-too-large";             // 413
    public const string BatchTooLarge = "batch-too-large";             // 413
    public const string BackupTooLarge = "backup-too-large";           // 413
    public const string UnsupportedMediaType = "unsupported-media-type"; // 415
    public const string BackupInvalid = "backup-invalid";              // 422
    public const string BackupEncrypted = "backup-encrypted";          // 422
    public const string BackupNewerVersion = "backup-newer-version";   // 422
    public const string NotABackup = "not-a-backup";                   // 422
    public const string AppTooOld = "app-too-old";                     // 426
    public const string RateLimited = "rate-limited";                  // 429
    public const string InternalError = "internal-error";              // 500

    public const string ContentType = "application/problem+json";

    /// <summary>A Problem Details result for an MVC action or filter.</summary>
    public static ObjectResult Problem(int status, string type, string? title = null, string? detail = null) =>
        new(new ProblemDetails { Status = status, Type = type, Title = title ?? DefaultTitle(type), Detail = detail })
        {
            StatusCode = status,
            ContentTypes = { ContentType },
        };

    /// <summary>409 dataset-reset, with the current epoch as <c>datasetEpoch</c> so the phone can store it and pull from 0.</summary>
    public static ObjectResult DatasetResetProblem(string datasetEpoch)
    {
        var result = Problem(StatusCodes.Status409Conflict, DatasetReset,
            detail: "The server data was replaced. Delete the synced data and pull again from 0.");
        ((ProblemDetails)result.Value!).Extensions["datasetEpoch"] = datasetEpoch;
        return result;
    }

    /// <summary>Short English title shown when the caller gives none.</summary>
    public static string DefaultTitle(string type) => type switch
    {
        ValidationFailed => "The request is not valid",
        InvalidCredentials => "Wrong username or password",
        AccountLocked => "Too many failed attempts",
        AccountDisabled => "This account is disabled",
        Unauthorized => "Authentication required",
        TokenExpired => "The access token has expired",
        SessionRevoked => "This session has ended",
        NotAllowed => "Not allowed",
        GuestSignupOff => "Guest access is turned off",
        NotFound => "Not found",
        InviteInvalid => "This code is not valid",
        NoPromotion => "No pending promotion",
        SetupDone => "The server is already set up",
        UsernameTaken => "This username is taken",
        RoleNotAllowed => "This role is not allowed here",
        UsePromotion => "Use a promotion for Guests",
        DatasetReset => "The server data was replaced",
        GuestLimitReached => "The Guest limit is reached",
        InviteExpired => "This invitation has expired",
        InviteUsed => "This invitation has already been used",
        ResetExpired => "This reset code has expired",
        ResetUsed => "This reset code has already been used",
        PromotionExpired => "This promotion has expired",
        AppTooOld => "App version not supported",
        RateLimited => "Too many requests",
        InternalError => "Internal error",
        _ => "Error",
    };
}

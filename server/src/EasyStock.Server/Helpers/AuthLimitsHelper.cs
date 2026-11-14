namespace EasyStock.Server.Helpers;

/// <summary>Lifetimes, limits and lock keys of the account system (docs/03-server-spec.md, "Accounts").</summary>
public static class AuthLimitsHelper
{
    public const string Audience = "easystock-app";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>A refresh token not used for this long ends its session.</summary>
    public static readonly TimeSpan RefreshIdleLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// How long after a rotation the replaced refresh token still answers with the new pair instead of revoking
    /// the session: covers two refreshes sent at once and a retry after a lost answer.
    /// </summary>
    public static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(10);

    /// <summary>last_used_at and last_seen_at are written at most this often.</summary>
    public static readonly TimeSpan ActivityWriteInterval = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan PromotionLifetime = TimeSpan.FromDays(7);

    /// <summary>Failed passwords per account and client address before that pair is locked.</summary>
    public const int MaxFailedLogins = 5;

    /// <summary>
    /// Failed passwords per account from all addresses within <see cref="LockoutDuration"/> before the account is locked
    /// everywhere: bounds guessing spread over many addresses.
    /// </summary>
    public const int MaxFailedLoginsPerAccount = 50;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public const int PasswordMinLength = 10;
    public const int PasswordMaxLength = 128;
    public const int DisplayNameMaxLength = 50;
    public const int DeviceNameMaxLength = 100;

    public const int DefaultGuestLimit = 10;
    public const int GuestSignupsPerIpPerHour = 5;

    /// <summary>Transaction-level advisory lock taken by setup, so only one first Admin is ever created.</summary>
    public const long SetupLockKey = 0x4553_5345_5455; // "ESSETU"

    /// <summary>Transaction-level advisory lock around the Guest count of a signup.</summary>
    public const long GuestSignupLockKey = 0x4553_4755_4553; // "ESGUES"

    /// <summary>Transaction-level advisory lock around changes that could remove the last active Admin.</summary>
    public const long AdminChangeLockKey = 0x4553_4144_4D49; // "ESADMI"

    /// <summary>Values of <c>device_session.revoke_reason</c>.</summary>
    public static class RevokeReasons
    {
        public const string Logout = "logout";
        public const string TokenReuse = "token-reuse";
        public const string Expired = "expired";
        public const string PasswordReset = "password-reset";
        public const string PasswordChanged = "password-changed";
        public const string Disabled = "disabled";
        public const string Deleted = "deleted";
        public const string Admin = "admin";
    }
}

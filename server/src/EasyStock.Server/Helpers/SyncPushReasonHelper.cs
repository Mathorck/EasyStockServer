namespace EasyStock.Server.Helpers;

/// <summary><see cref="Models.SyncPushResult.Reason"/> values of a rejected row (the app shows unknown ones as is).</summary>
public static class SyncPushReasonHelper
{
    public const string NotAllowed = "not-allowed";
    public const string Invalid = "invalid";
    public const string MissingReference = "missing-reference";
    public const string ReviewOpen = "review-open";
    public const string Conflict = "conflict";
}

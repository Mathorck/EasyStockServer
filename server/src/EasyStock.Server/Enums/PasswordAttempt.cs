
namespace EasyStock.Server.Enums;

/// <summary>Outcome of <see cref="PasswordAttemptHandler.CheckAsync"/>.</summary>
public enum PasswordAttempt
{
    Right,
    Wrong,
    /// <summary>This attempt was wrong and locked the account for this address (or everywhere).</summary>
    WrongAndLocked,
    /// <summary>The account was already locked for this address; the password was not judged.</summary>
    Locked,
}

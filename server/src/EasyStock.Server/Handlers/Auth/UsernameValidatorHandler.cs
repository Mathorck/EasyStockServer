using System.Text.RegularExpressions;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using Microsoft.AspNetCore.Identity;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// Replaces Identity's UserValidator, which rejects a null username: full accounts need a username of 3 to 32
/// letters, digits, dots, dashes and underscores, unique without case; Guests (and tombstones) have none.
/// </summary>
public sealed partial class UsernameValidatorHandler : IUserValidator<AppUser>
{
    public const string InvalidCode = "InvalidUserName";
    public const string DuplicateCode = "DuplicateUserName";

    [GeneratedRegex("^[A-Za-z0-9._-]{3,32}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? username) => username is not null && Pattern().IsMatch(username);

    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
    {
        var name = await manager.GetUserNameAsync(user);
        if (name is null)
            return user.Kind == UserKind.Guest || user.DeletedAt is not null
                ? IdentityResult.Success
                : IdentityResult.Failed(Invalid());
        if (user.Kind == UserKind.Guest || !IsValid(name))
            return IdentityResult.Failed(Invalid());
        var owner = await manager.FindByNameAsync(name);
        return owner is not null && owner.Id != user.Id
            ? IdentityResult.Failed(new IdentityError { Code = DuplicateCode, Description = "This username is taken." })
            : IdentityResult.Success;
    }

    private static IdentityError Invalid() => new()
    {
        Code = InvalidCode,
        Description = "Usernames are 3 to 32 letters, digits, dots, dashes or underscores.",
    };
}

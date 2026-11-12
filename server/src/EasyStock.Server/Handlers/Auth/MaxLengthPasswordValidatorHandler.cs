using System.Text.RegularExpressions;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using Microsoft.AspNetCore.Identity;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>Passwords are at most 128 characters (the minimum of 10 is Identity's RequiredLength).</summary>
public sealed class MaxLengthPasswordValidatorHandler : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password) =>
        Task.FromResult(password is { Length: > AuthLimitsHelper.PasswordMaxLength }
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"Passwords must be at most {AuthLimitsHelper.PasswordMaxLength} characters.",
            })
            : IdentityResult.Success);
}

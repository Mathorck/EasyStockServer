
namespace EasyStock.Server.Tests.Models;

/// <summary>A user created for a test, with the secrets the test may need again.</summary>
public sealed record TestUser(string Username, string Password, TokenAnswer Tokens)
{
    public Guid Id => Tokens.User.Id;
    public string AccessToken => Tokens.AccessToken;
    public string RefreshToken => Tokens.RefreshToken;
}

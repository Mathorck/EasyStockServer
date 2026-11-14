using EasyStock.Server.Handlers.Auth;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class UsernameValidatorTests
{
    [Theory]
    [InlineData("abc", true)]
    [InlineData("a.b-c_D9", true)]
    [InlineData("ab", false)]
    [InlineData("alice\n", false)]
    [InlineData("alice\r\n", false)]
    [InlineData("\nalice", false)]
    [InlineData("ali ce", false)]
    [InlineData(null, false)]
    public void Only_plain_usernames_are_valid(string? username, bool valid) =>
        Assert.Equal(valid, UsernameValidatorHandler.IsValid(username));

    [Fact]
    public void Length_is_3_to_32()
    {
        Assert.True(UsernameValidatorHandler.IsValid(new string('a', 32)));
        Assert.False(UsernameValidatorHandler.IsValid(new string('a', 33)));
    }
}

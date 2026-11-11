using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class StartupTests
{
    [Theory]
    [InlineData(null, "https://stock.example.org", "EASYSTOCK_DB")]
    [InlineData("Host=localhost;Database=x", null, "EASYSTOCK_PUBLIC_URL")]
    [InlineData("Host=localhost;Database=x", "not-a-url", "EASYSTOCK_PUBLIC_URL")]
    public void Missing_or_invalid_required_setting_fails_startup(string? db, string? publicUrl, string expected)
    {
        using var factory = new ApiFactory(db, publicUrl);
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains(expected, Flatten(ex));
    }

    private static string Flatten(Exception? e)
    {
        var parts = new List<string>();
        for (; e is not null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" | ", parts);
    }
}

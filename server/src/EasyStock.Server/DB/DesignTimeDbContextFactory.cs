using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EasyStock.Server.DB;

/// <summary>Used by `dotnet ef migrations add`: no EASYSTOCK_* variables or running database needed.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EasyStockDbContext>
{
    public EasyStockDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>();
        EasyStockDbContext.Configure(options, "Host=localhost;Database=easystock");
        return new EasyStockDbContext(options.Options);
    }
}

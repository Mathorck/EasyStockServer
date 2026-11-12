using EasyStock.Server.DB.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasyStock.Server.DB;

/// <summary>
/// The whole schema: the tables shared with the app, plus the server-only tables (users is the Identity user
/// table, without the claim, login and token tables).
/// </summary>
public sealed class EasyStockDbContext(DbContextOptions<EasyStockDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    // Shared with the app (docs/schema.dbml).
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<CategoryAttribute> CategoryAttributes => Set<CategoryAttribute>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockReview> StockReviews => Set<StockReview>();
    public DbSet<StockReviewLine> StockReviewLines => Set<StockReviewLine>();

    // Server only.
    public DbSet<DeviceSession> DeviceSessions => Set<DeviceSession>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<PublicInvite> PublicInvites => Set<PublicInvite>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
    public DbSet<SetupState> SetupStates => Set<SetupState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Usernames and passwords only: no external logins, claims or Identity-managed tokens.
        modelBuilder.Ignore<IdentityUserClaim<Guid>>();
        modelBuilder.Ignore<IdentityUserLogin<Guid>>();
        modelBuilder.Ignore<IdentityUserToken<Guid>>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EasyStockDbContext).Assembly);
    }

    /// <summary>Same provider setup as the app, for both the service and the design-time factory.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention()
            // IdentityUserContext maps claims, logins, tokens, e-mail and phone; the model ignores them on purpose.
            .ConfigureWarnings(w => w.Ignore(CoreEventId.MappedEntityTypeIgnoredWarning, CoreEventId.MappedPropertyIgnoredWarning));
}

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

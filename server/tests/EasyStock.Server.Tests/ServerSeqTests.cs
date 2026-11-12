using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class ServerSeqTests(PostgresFixture pg)
{
    [Fact]
    public async Task Insert_and_update_take_a_new_seq_from_the_global_sequence()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var category = new Category { Id = Guid.NewGuid(), Name = $"seq-{Guid.NewGuid():N}", CreatedAt = 1, UpdatedAt = 1 };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        var inserted = category.ServerSeq;
        Assert.True(inserted > 0);

        var location = new Location { Id = Guid.NewGuid(), Name = "Shelf", CreatedAt = 1, UpdatedAt = 1 };
        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        Assert.True(location.ServerSeq > inserted, "one sequence shared by every table");

        category.UpdatedAt = 2;
        await db.SaveChangesAsync(ct);
        Assert.True(category.ServerSeq > location.ServerSeq, "an update takes a new seq");

        // A seq sent by a client is ignored.
        await db.Database.ExecuteSqlAsync($"UPDATE category SET server_seq = 1 WHERE id = {category.Id}", ct);
        Assert.True(await db.Categories.Where(c => c.Id == category.Id).Select(c => c.ServerSeq).SingleAsync(ct) > category.ServerSeq);
    }

    [Fact]
    public async Task Sync_lock_needs_a_transaction()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.AcquireSyncLockAsync(ct));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireSyncLockAsync(ct);
        await db.AcquireSyncLockAsync(ct); // re-entrant within the transaction
        await tx.CommitAsync(ct);
    }
}

using EasyStock.Server.DB;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class UserSeqTests(PostgresFixture pg)
{
    [Fact]
    public async Task Users_take_a_new_seq_only_when_synced_fields_change()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        var user = await factory.CreateUserAsync();

        async Task<long> Seq()
        {
            long seq = 0;
            await factory.WithDb(async db => seq = await db.Users.Where(u => u.Id == user.Id).Select(u => u.ServerSeq).SingleAsync(ct));
            return seq;
        }

        Task Sql(FormattableString sql) => factory.WithDb(db => db.Database.ExecuteSqlAsync(sql, ct));

        var inserted = await Seq();
        Assert.True(inserted > 0);

        await Sql($"UPDATE users SET last_seen_at = now(), access_failed_count = 3 WHERE id = {user.Id}");
        Assert.Equal(inserted, await Seq());

        await Sql($"UPDATE users SET display_name = 'Renamed' WHERE id = {user.Id}");
        var renamed = await Seq();
        Assert.True(renamed > inserted);

        await Sql($"UPDATE users SET deleted_at = now() WHERE id = {user.Id}");
        Assert.True(await Seq() > renamed);
    }
}

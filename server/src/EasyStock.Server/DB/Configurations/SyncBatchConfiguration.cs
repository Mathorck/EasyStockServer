using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class SyncBatchConfiguration : IEntityTypeConfiguration<SyncBatch>
{
    public void Configure(EntityTypeBuilder<SyncBatch> e)
    {
        e.ToTable("sync_batch");
        e.HasKey(x => x.BatchId);
        e.Property(x => x.BatchId).ValueGeneratedNever();
        // A batch goes with its user and session (users are soft-deleted; sessions are deleted by cleanup).
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<DeviceSession>().WithMany().HasForeignKey(x => x.DeviceSessionId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.UserId);
        e.HasIndex(x => x.DeviceSessionId);
        e.HasIndex(x => x.ReceivedAt);
        e.Property(x => x.RequestHash).HasMaxLength(32);
    }
}

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

internal sealed class AttributeAliasConfiguration : IEntityTypeConfiguration<AttributeAlias>
{
    public void Configure(EntityTypeBuilder<AttributeAlias> e)
    {
        e.ToTable("attribute_alias");
        e.HasKey(x => x.AliasId);
        e.Property(x => x.AliasId).ValueGeneratedNever();
        e.Ref<AttributeAlias, AttributeDefinition>(nameof(AttributeAlias.AttributeId), required: true);
        e.HasIndex(x => x.AttributeId);
    }
}

internal sealed class LowStockStateConfiguration : IEntityTypeConfiguration<LowStockState>
{
    public void Configure(EntityTypeBuilder<LowStockState> e)
    {
        e.ToTable("low_stock_state");
        e.HasKey(x => x.ArticleId);
        e.Property(x => x.ArticleId).ValueGeneratedNever();
        e.Ref<LowStockState, Article>(nameof(LowStockState.ArticleId), required: true);
    }
}

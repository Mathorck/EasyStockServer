using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

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

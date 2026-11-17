using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> e)
    {
        e.ToTable("stock_movement");
        e.Property(x => x.Reason).HasMaxLength(30);
        e.Ref<StockMovement, Article>(nameof(StockMovement.ArticleId), required: true);
        e.Ref<StockMovement, Item>(nameof(StockMovement.ItemId), required: false);
        e.Ref<StockMovement, Location>(nameof(StockMovement.LocationId), required: true);
        e.HasIndex(x => new { x.ArticleId, x.LocationId });
        // App schema v12 covering indexes (stock sums and article history), mirrored for the drift test.
        e.HasIndex(x => new { x.ArticleId, x.LocationId, x.Quantity });
        e.HasIndex(x => new { x.LocationId, x.ArticleId, x.Quantity });
        e.HasIndex(x => new { x.ArticleId, x.OccurredAt, x.CreatedAt, x.Id });
        e.HasIndex(x => x.ItemId);
        e.HasIndex(x => x.CreatedAt);
        // Pull names only users some movement names (PullUserDirectoryHandler).
        e.HasIndex(x => x.CreatedBy);
        e.ServerSeq();
    }
}

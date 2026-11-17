using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class StockReviewConfiguration : IEntityTypeConfiguration<StockReview>
{
    /// <summary>At most one open review per location (docs/04-server-implementation-plan.md 1.3).</summary>
    public const string OpenReviewIndex = "ux_stock_review_location_id_open";

    public void Configure(EntityTypeBuilder<StockReview> e)
    {
        e.ToTable("stock_review");
        e.Property(x => x.Status).HasMaxLength(20);
        e.Ref<StockReview, Location>(nameof(StockReview.LocationId), required: true);
        e.HasIndex(x => x.LocationId);
        // Push checks it first (review-open); the index is the backstop.
        e.HasIndex(x => x.LocationId, OpenReviewIndex).IsUnique().HasFilter("status = 'open' AND deleted_at IS NULL")
            .HasDatabaseName(OpenReviewIndex);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

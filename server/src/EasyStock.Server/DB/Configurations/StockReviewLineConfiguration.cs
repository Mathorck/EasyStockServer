using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class StockReviewLineConfiguration : IEntityTypeConfiguration<StockReviewLine>
{
    public void Configure(EntityTypeBuilder<StockReviewLine> e)
    {
        e.ToTable("stock_review_line");
        e.Property(x => x.Result).HasMaxLength(20);
        e.Property(x => x.PendingItem).HasColumnType(Shared.Json);
        e.Ref<StockReviewLine, StockReview>(nameof(StockReviewLine.ReviewId), required: true);
        e.Ref<StockReviewLine, Article>(nameof(StockReviewLine.ArticleId), required: true);
        e.Ref<StockReviewLine, Item>(nameof(StockReviewLine.ItemId), required: false);
        e.Ref<StockReviewLine, Location>(nameof(StockReviewLine.LocationId), required: true);
        e.Ref<StockReviewLine, Location>(nameof(StockReviewLine.FoundLocationId), required: false);
        e.HasIndex(x => x.ReviewId);
        e.HasIndex(x => x.ArticleId);
        e.HasIndex(x => x.ItemId);
        e.HasIndex(x => x.LocationId);
        e.HasIndex(x => x.FoundLocationId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

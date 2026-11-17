using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public const int OrderUrlMax = 2048;

    public void Configure(EntityTypeBuilder<Article> e)
    {
        e.ToTable("article");
        e.Property(x => x.Sku).HasMaxLength(50);
        e.Property(x => x.Name).HasMaxLength(255);
        e.Property(x => x.Tracking).HasMaxLength(10);
        e.Property(x => x.Unit).HasMaxLength(20);
        e.Property(x => x.Barcode).HasMaxLength(64);
        e.Property(x => x.ImageSha256).HasMaxLength(64).IsFixedLength();
        e.Property(x => x.Attributes).HasColumnType(Shared.Json);
        e.Property(x => x.OrderUrl).HasMaxLength(OrderUrlMax);
        e.Ref<Article, Category>(nameof(Article.CategoryId), required: false);
        e.HasIndex(x => x.Sku).IsUnique();
        e.HasIndex(x => x.Barcode).IsUnique();
        e.HasIndex(x => x.CategoryId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

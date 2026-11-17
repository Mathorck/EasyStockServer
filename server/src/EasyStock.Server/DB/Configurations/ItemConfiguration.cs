using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> e)
    {
        e.ToTable("item");
        e.Property(x => x.SerialNumber).HasMaxLength(100);
        e.Property(x => x.Status).HasMaxLength(20);
        e.Property(x => x.Attributes).HasColumnType(Shared.Json);
        e.Ref<Item, Article>(nameof(Item.ArticleId), required: true);
        e.Ref<Item, Location>(nameof(Item.LocationId), required: false);
        e.HasIndex(x => x.SerialNumber).IsUnique();
        e.HasIndex(x => x.ArticleId);
        e.HasIndex(x => x.LocationId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

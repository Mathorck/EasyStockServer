using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> e)
    {
        e.ToTable("category");
        e.Property(x => x.Name).HasMaxLength(100);
        e.HasIndex(x => x.Name).IsUnique();
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

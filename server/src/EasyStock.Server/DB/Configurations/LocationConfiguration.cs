using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> e)
    {
        e.ToTable("location");
        e.Property(x => x.Name).HasMaxLength(100);
        e.Property(x => x.Icon).HasMaxLength(32);
        e.Property(x => x.Color).HasMaxLength(32);
        e.Property(x => x.Barcode).HasMaxLength(64);
        e.Ref<Location, Location>(nameof(Location.ParentId), required: false);
        e.HasIndex(x => x.Barcode).IsUnique();
        e.HasIndex(x => x.ParentId);
        e.HasIndex(x => x.UpdatedAt);
        e.ServerSeq();
    }
}

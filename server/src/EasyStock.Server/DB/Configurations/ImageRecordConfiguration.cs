using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class ImageRecordConfiguration : IEntityTypeConfiguration<ImageRecord>
{
    public void Configure(EntityTypeBuilder<ImageRecord> e)
    {
        e.ToTable("image");
        e.HasKey(x => x.Sha256);
        e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
    }
}

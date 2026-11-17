using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB.Configurations;

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> e)
    {
        e.ToTable("promotion");
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        e.Property(x => x.TargetRole).AsWire();
        e.Property(x => x.EndReason).HasMaxLength(20);
        // At most one pending promotion per user.
        e.HasIndex(x => x.UserId).IsUnique().HasFilter("ended_at IS NULL");
    }
}

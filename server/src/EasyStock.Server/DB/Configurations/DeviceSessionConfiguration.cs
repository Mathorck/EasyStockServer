using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB.Configurations;

internal sealed class DeviceSessionConfiguration : IEntityTypeConfiguration<DeviceSession>
{
    public void Configure(EntityTypeBuilder<DeviceSession> e)
    {
        e.ToTable("device_session");
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.Property(x => x.DeviceName).HasMaxLength(100);
        e.Property(x => x.RefreshTokenHash).Sha256();
        e.Property(x => x.PreviousTokenHash).Sha256();
        e.Property(x => x.RevokeReason).HasMaxLength(30);
        e.HasIndex(x => x.RefreshTokenHash).IsUnique();
        e.HasIndex(x => x.PreviousTokenHash).IsUnique();
        e.HasIndex(x => x.UserId);
    }
}

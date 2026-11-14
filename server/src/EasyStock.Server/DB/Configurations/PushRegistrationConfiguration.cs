using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Server.DB.Configurations;

internal sealed class PushRegistrationConfiguration : IEntityTypeConfiguration<PushRegistration>
{
    public void Configure(EntityTypeBuilder<PushRegistration> e)
    {
        e.ToTable("push_registration");
        e.HasKey(x => x.DeviceSessionId);
        // A session's endpoint goes with it (revoked sessions are kept until the cleanup job deletes them).
        e.HasOne<DeviceSession>().WithOne().HasForeignKey<PushRegistration>(x => x.DeviceSessionId).OnDelete(DeleteBehavior.Cascade);
        e.Property(x => x.Endpoint).IsRequired(); // text; the length limit is checked in PushController
    }
}

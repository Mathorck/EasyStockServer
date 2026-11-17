using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB.Configurations;

internal sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> e)
    {
        e.ToTable("invite");
        e.Property(x => x.TokenHash).Sha256();
        e.Property(x => x.Role).AsWire();
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.TokenHash).IsUnique();
    }
}

using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB.Configurations;

internal sealed class PublicInviteConfiguration : IEntityTypeConfiguration<PublicInvite>
{
    public void Configure(EntityTypeBuilder<PublicInvite> e)
    {
        e.ToTable("public_invite", t => t.HasCheckConstraint("ck_public_invite_singleton", "id = 1"));
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.TokenHash).Sha256();
        e.HasIndex(x => x.TokenHash).IsUnique();
    }
}

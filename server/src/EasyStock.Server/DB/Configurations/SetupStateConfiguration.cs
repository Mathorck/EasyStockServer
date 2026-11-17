using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB.Configurations;

internal sealed class SetupStateConfiguration : IEntityTypeConfiguration<SetupState>
{
    public void Configure(EntityTypeBuilder<SetupState> e)
    {
        e.ToTable("setup_state", t => t.HasCheckConstraint("ck_setup_state_singleton", "id = 1"));
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.TokenHash).Sha256();
    }
}
